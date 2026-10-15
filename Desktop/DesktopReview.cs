using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Text.Json;
using WplaceColorWatch;

namespace Wplace.Desktop;

/// <summary>Opt-in UI render/contract checks. Runs only with the read-only engine.</summary>
internal static class DesktopReview
{
    internal static bool Failed { get; private set; }
    internal static async Task RunAsync(MainWindow main, string directory)
    {
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        try
        {
            Directory.CreateDirectory(directory);
            Check(main.Engine.ReadOnly, "Review mode never enables desktop input or global hooks");
            Check(!main.CompactMode && main.GuideEntryVisible, "Application opens in full mode with the feature guide entry visible");
            Check(WindowsEngineHost.Palette.Count == 63, "63 canonical colors available");
            await main.Engine.SetParametersAsync(1, 12, false, true);
            Check(main.Engine.Snapshot.Step == 12 && main.Engine.Snapshot.Extreme, "CPU, step and speed reach the original engine");
            await main.Engine.ExecuteAsync(DesktopCommand.AddWanted, "#ED1C24");
            Check(main.Engine.Snapshot.Wanted.SequenceEqual(new[] { "#ED1C24" }), "Wanted rule reaches the original rule set");
            await main.Engine.ExecuteAsync(DesktopCommand.AddExcluded, "#ED1C24");
            Check(main.Engine.Snapshot.Wanted.Count == 0 && main.Engine.Snapshot.Excluded.Count == 1 && main.Engine.Snapshot.EffectiveCount == 62, "Moving a color between rules stays exclusive");
            await main.Engine.ExecuteAsync(DesktopCommand.SelectAllColors);
            Check(main.Engine.Snapshot.Wanted.Count == 63 && main.Engine.Snapshot.Excluded.Count == 0, "Select all clears exclusions");
            await main.Engine.ExecuteAsync(DesktopCommand.ClearWanted);
            Check(main.Engine.Snapshot.Wanted.Count == 0 && main.Engine.Snapshot.EffectiveCount == 63, "Empty wanted means default whole palette");
            await main.Engine.SetParametersAsync(1, 10, false, false);
            main.ApplyState(main.Engine.Snapshot); main.SetCompact(false, false);
            await Task.Delay(150);
            Check(main.GuideEntryVisible, "Feature introduction exists in full mode");
            Capture(main, directory, "01-full");
            main.SetCompact(true, false); await Task.Delay(150);
            Check(!main.GuideEntryVisible, "Feature introduction is absent in compact mode");
            Capture(main, directory, "02-compact");
            var boxes = new List<object>();
            foreach (var percent in new[] { 0, 9, 62, 100 })
            {
                main.ApplyState(main.Engine.Snapshot with { Percent = percent, Remaining = percent == 100 ? "01:23:45" : "00:42" });
                Layout(main);
                var expand = main.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExpandLayout");
                boxes.Add(new { Percent = percent, Width = ((Control)main.Content!).Bounds.Width, ExpandX = expand.TranslatePoint(default, main)?.X });
                Capture(main, directory, "compact-" + percent.ToString("D3"));
            }
            File.WriteAllText(Path.Combine(directory, "layout.json"), JsonSerializer.Serialize(boxes, new JsonSerializerOptions { WriteIndented = true }));
            main.SetCompact(false, false); main.ApplyState(main.Engine.Snapshot); await Task.Delay(100);
            await main.Engine.ExecuteAsync(DesktopCommand.OpenColors);
            var colors = new ColorManagerWindow(main); colors.UpdateState(main.Engine.Snapshot); colors.Show(main); await Task.Delay(120); Capture(colors, directory, "03-colors");
            colors.UpdateState(main.Engine.Snapshot with { Picking = "Wanted", PickMessage = "将鼠标停在画布上，按 A 选择。此图为界面状态预览。" }); await Task.Delay(80); Capture(colors, directory, "04-picking"); colors.Close();
            await main.Engine.ExecuteAsync(DesktopCommand.CloseColors);
            var notes = new NotesWindow(); notes.Show(main); await Task.Delay(100); Capture(notes, directory, "05-release-notes"); notes.Close();
            var guide = new FeatureGuideWindow(); guide.Show(main); await Task.Delay(120);
            for (int i = 0; i < FeatureGuideWindow.Topics.Length; i++)
            {
                guide.SelectTopic(i); await Task.Delay(30); Capture(guide, directory, "guide-" + FeatureGuideWindow.Topics[i].Id);
            }
            Check(FeatureGuideWindow.Topics.Length == 10, "Ten documented feature pages with illustrations"); guide.Close();
            var prompts = new[]
            {
                new EnginePrompt("遗漏检测", "强烈建议在已经填涂好的情况下进行遗漏检测功能。\n\n是否继续运行遗漏检测？", true, "不再提示"),
                new EnginePrompt("遗漏检测", "检测到 3 处孤岛（12 个像素点）。\n请确认这些点是否需要补涂，确认后将自动补涂。", true, null),
                new EnginePrompt("遗漏检测", "当前颜色规则下未检测到遗漏点", false, null),
                new EnginePrompt("提示", "请框选范围", false, null)
            };
            for (int i = 0; i < prompts.Length; i++)
            {
                var prompt = new PromptWindow(prompts[i]); prompt.Show(main); await Task.Delay(80); Capture(prompt, directory, "prompt-" + i); prompt.Close();
            }
            // Exercise actual UI handlers and modal ownership, not just standalone
            // renders. Read-only mode ensures this never generates desktop input.
            Click(main, "AutoWorkers");
            await Until(() => main.State.Workers == Math.Max(1, Environment.ProcessorCount / 2));
            Check(main.Engine.Snapshot.Workers == Math.Max(1, Environment.ProcessorCount / 2), "Automatic CPU selection reaches the engine and visible field");
            var stepInput = main.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "FullStepInput");
            stepInput.Text = "17"; SendKey(stepInput, Key.Enter);
            await Until(() => main.State.Step == 17);
            Check(main.Engine.Snapshot.Step == 17, "Editing the visible step field updates the engine");
            stepInput.Text = "0"; SendKey(stepInput, Key.Enter);
            Check(stepInput.Text == "17" && main.Engine.Snapshot.Step == 17, "Invalid visible input restores the previous value");
            ToolTip.SetIsOpen(stepInput, false);
            Click(main, "FullExtreme"); await Until(() => main.State.Extreme);
            Click(main, "CompactLayout"); await Until(() => main.CompactMode && main.Engine.Snapshot.Compact);
            Check(!main.GuideEntryVisible && !FindButton(main, "ReleaseNotes").IsEffectivelyVisible, "Real layout button hides full-only entries");
            Check(main.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).All(b => b.GetVisualAncestors().OfType<Control>().Any(c => ToolTip.GetTip(c) != null)), "Every visible compact button has a hover description");
            Click(main, "ExpandLayout"); await Until(() => !main.CompactMode && !main.Engine.Snapshot.Compact);
            Check(main.GuideEntryVisible, "Expanding restores the feature guide entry");

            Click(main, "FullColors"); await Until(() => main.OwnedWindows.OfType<ColorManagerWindow>().Any());
            var liveColors = main.OwnedWindows.OfType<ColorManagerWindow>().Single();
            liveColors.UpdateLayout();
            var red = liveColors.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "色板 #ED1C24");
            Check(red.IsEffectivelyEnabled, "Modal color palette remains interactive while its owner is disabled");
            red.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => main.State.Wanted.Contains("#ED1C24"));
            Check(main.Engine.Snapshot.Wanted.Contains("#ED1C24"), "Actual palette click updates the core wanted rules");
            Click(liveColors, "PaletteExcluded"); red.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => main.State.Excluded.Contains("#ED1C24") && main.State.Wanted.Count == 0);
            Check(main.Engine.Snapshot.EffectiveCount == 62, "Actual exclude tab moves the palette color into exclusions");
            await Task.Delay(180);
            Capture(liveColors, directory, "colors-with-rule");
            Check(liveColors.GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == "移除 #ED1C24" && b.IsEffectivelyVisible && b.Bounds.Width > 0), $"Selected color chip is visible and removable after the live rule update (window={liveColors.IsVisible}, engine={main.Engine.Snapshot.ColorsOpen})");
            var remove = liveColors.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "移除 #ED1C24" && b.IsEffectivelyVisible);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => main.State.Excluded.Count == 0);
            Check(main.Engine.Snapshot.EffectiveCount == 63, "Removing the selected color chip restores the original palette rules");
            SendKey(liveColors, Key.Escape); await Until(() => !main.Engine.Snapshot.ColorsOpen && !liveColors.IsVisible);
            Check(!main.Engine.Snapshot.ColorsOpen, "Closing the real color dialog clears the engine modal state");
            Click(main, "FullColors");
            await Until(() => main.OwnedWindows.OfType<ColorManagerWindow>().Any());
            var reopenedColors = main.OwnedWindows.OfType<ColorManagerWindow>().Single();
            await Task.Delay(180);
            Check(reopenedColors.IsVisible && main.Engine.Snapshot.ColorsOpen, "Reopening color management survives queued state notifications");
            SendKey(reopenedColors, Key.Escape);
            await Until(() => !main.Engine.Snapshot.ColorsOpen && !reopenedColors.IsVisible);

            Click(main, "FeatureGuide"); await Until(() => main.OwnedWindows.OfType<FeatureGuideWindow>().Any());
            var liveGuide = main.OwnedWindows.OfType<FeatureGuideWindow>().Single(); liveGuide.UpdateLayout();
            Click(liveGuide, "GuideTopic8");
            Check(liveGuide.SelectedIndex == 8, "Real feature directory buttons switch articles");
            SendKey(liveGuide, Key.Escape); await Until(() => !liveGuide.IsVisible);
            Check(!main.Engine.HotkeysSuspended, "Esc closes the guide and restores global-hotkey availability");

            Click(main, "ReleaseNotes"); await Until(() => main.OwnedWindows.OfType<NotesWindow>().Any());
            var liveNotes = main.OwnedWindows.OfType<NotesWindow>().Single(); liveNotes.UpdateLayout();
            Click(liveNotes, "AcknowledgeNotes"); await Until(() => !liveNotes.IsVisible && !main.Engine.HotkeysSuspended);
            Check(!main.Engine.HotkeysSuspended, "Manual notes entry and fixed acknowledgement button work");

            Click(main, "FullStart"); await Until(() => main.OwnedWindows.OfType<PromptWindow>().Any());
            var livePrompt = main.OwnedWindows.OfType<PromptWindow>().Single();
            Check(main.Engine.PromptOpen, "Engine prompts open as actual Avalonia modal windows");
            SendKey(livePrompt, Key.Escape); await Until(() => !main.Engine.PromptOpen && !livePrompt.IsVisible);
            Check(!main.Engine.Snapshot.Busy, "Dismissing a prompt returns the core to idle");
            var recommendationTask = main.ShowPromptAsync(new EnginePrompt("遗漏检测", "是否继续？", true, "不再提示"));
            await Until(() => main.OwnedWindows.OfType<PromptWindow>().Any());
            var recommendation = main.OwnedWindows.OfType<PromptWindow>().Single(); recommendation.UpdateLayout();
            recommendation.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
            Click(recommendation, "CancelPrompt");
            var declined = await recommendationTask;
            Check(!declined.Accepted && declined.DontShowAgain, "Declining a recommendation retains the original checkbox semantics");
            File.WriteAllText(Path.Combine(directory, "checks.json"), JsonSerializer.Serialize(new { Passed = checks.Count, Checks = checks, RenderScaling = main.RenderScaling }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            Failed = true;
            try { File.WriteAllText(Path.Combine(directory, "failure.txt"), error.ToString()); }
            catch (Exception writeError) { Logger.Error("[desktop-review] " + error + "\n" + writeError); }
        }
    }
    private static Button FindButton(Window window, string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
    private static void Click(Window window, string name)
    {
        var button = FindButton(window, name);
        if (!button.IsEffectivelyEnabled || !button.IsEffectivelyVisible) throw new InvalidOperationException("Button cannot be used: " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static void SendKey(InputElement target, Key key) => target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }
    private static Control Layout(Window window)
    {
        var content = (Control)window.Content!;
        window.UpdateLayout();
        return content;
    }
    private static void Capture(Window window, string directory, string name)
    {
        var content = Layout(window);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(content.Bounds.Width * 1.25), (int)Math.Ceiling(content.Bounds.Height * 1.25)), new Vector(120, 120));
        bitmap.Render(content); bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
