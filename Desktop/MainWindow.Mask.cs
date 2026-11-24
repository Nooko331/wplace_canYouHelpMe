using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WplaceColorWatch;

namespace Wplace.Desktop;

public sealed partial class MainWindow
{
    private readonly Canvas maskGuide = new() { Background = Brush.Parse("#C8000000"), ClipToBounds = true };
    private readonly TaskCompletionSource maskChosen = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<(MaskType Type, RadioButton Radio)> maskChoices = [];
    private readonly TextBlock maskPrompt = Ui.Text("请选择当前使用的蒙版类型", 18, true);
    private Border maskSection = null!, guideSection = null!;

    private Border BuildMaskSection(bool guide)
    {
        var choices = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        foreach (var type in new[] { MaskType.BlueMarble, MaskType.Official })
        {
            var title = type == MaskType.BlueMarble ? "Blue Marble" : "官方蒙版";
            var radio = new RadioButton
            {
                Content = title, GroupName = guide ? "FirstRunMask" : "CurrentMask",
                Name = (guide ? "GuideMask" : "FullMask") + type,
                HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 40,
                Classes = { "mask" }, Foreground = Ui.Navy, FontWeight = FontWeight.SemiBold
            };
            AutomationProperties.SetName(radio, title);
            AutomationProperties.SetAutomationId(radio, radio.Name);
            radio.Click += (_, _) => Run(async () =>
            {
                try { await Engine.SetMaskAsync(type); }
                finally { ApplyState(Engine.Snapshot); }
            });
            maskChoices.Add((type, radio));
            Grid.SetColumn(radio, choices.Children.Count);
            choices.Children.Add(radio);
            if (!guide) editable.Add(radio);
        }
        return new Border { Child = Ui.Column(Section("layers", "蒙版类型"), choices), Background = Brushes.White };
    }

    private void BuildMaskGuide(Grid root)
    {
        guideSection = BuildMaskSection(true);
        guideSection.IsEnabled = false;
        maskPrompt.Foreground = Brushes.White;
        maskGuide.Children.Add(maskPrompt);
        maskGuide.Children.Add(guideSection);
        root.Children.Add(maskGuide);
        full.IsEnabled = false;
        Engine.HotkeysSuspended = true;
        maskGuide.LayoutUpdated += (_, _) => PositionMaskGuide();
    }

    private void PositionMaskGuide()
    {
        if (!maskGuide.IsVisible || maskSection.Bounds.Width <= 0) return;
        var origin = maskSection.TranslatePoint(default, maskGuide);
        if (origin == null) return;
        guideSection.Width = maskSection.Bounds.Width;
        var top = Math.Clamp(origin.Value.Y, maskPrompt.Bounds.Height + 12,
            Math.Max(maskPrompt.Bounds.Height + 12, maskGuide.Bounds.Height - maskSection.Bounds.Height - 8));
        Canvas.SetLeft(guideSection, origin.Value.X);
        Canvas.SetTop(guideSection, top);
        Canvas.SetLeft(maskPrompt, origin.Value.X);
        Canvas.SetTop(maskPrompt, top - maskPrompt.Bounds.Height - 6);
    }

    private void UpdateMaskGuide(DesktopSnapshot snapshot)
    {
        bool needsChoice = !snapshot.Mask.HasValue;
        foreach (var (type, radio) in maskChoices)
            radio.IsChecked = type == snapshot.Mask;
        if (!snapshot.Ready) return;
        if (needsChoice && !guideSection.IsEnabled)
        {
            guideSection.IsEnabled = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (!maskGuide.IsVisible) return;
                maskSection.BringIntoView();
                maskChoices.First(choice => choice.Radio.Name == "GuideMaskBlueMarble").Radio.Focus();
            }, DispatcherPriority.Loaded);
        }
        maskGuide.IsVisible = needsChoice;
        full.IsEnabled = !needsChoice;
        if (!needsChoice) maskChosen.TrySetResult();
        UpdateHotkeySuppression();
    }
}
