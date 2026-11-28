using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Wplace.Desktop;

public sealed partial class MainWindow
{
    private readonly Slider maskOpacity = new()
    {
        Name = "MaskOpacitySlider", Minimum = 0, Maximum = 100, Value = 100,
        TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10,
        MinHeight = 40, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly NumberField opacityInput = new("MaskOpacity", "蒙版不透明度百分比", 100, 94, minimum: 0, suffix: "%") { Maximum = 100 };
    private int opacityEdits;

    private Control BuildMaskSection()
    {
        AutomationProperties.SetName(maskOpacity, "蒙版不透明度");
        AutomationProperties.SetAutomationId(maskOpacity, maskOpacity.Name);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        row.Children.Add(maskOpacity);
        Grid.SetColumn(opacityInput, 1);
        row.Children.Add(opacityInput);
        editable.Add(maskOpacity);
        editable.Add(opacityInput);
        maskOpacity.ValueChanged += (_, _) =>
        {
            int percent = (int)Math.Round(maskOpacity.Value);
            opacityInput.UpdateValue(percent, force: true);
            if (applying || !initialStateApplied) return;
            opacityEdits++;
            Run(async () =>
            {
                try { await Engine.SetMaskOpacityAsync(percent); }
                finally
                {
                    if (--opacityEdits == 0) ApplyState(Engine.Snapshot);
                }
            });
        };
        maskOpacity.PointerWheelChanged += (_, e) =>
        {
            if (e.Delta.Y == 0) return;
            maskOpacity.Value = Math.Clamp(maskOpacity.Value + Math.Sign(e.Delta.Y), 0, 100);
            e.Handled = true;
        };
        opacityInput.Committed += percent => maskOpacity.Value = percent;
        return Ui.Column(Section("layers", "蒙版不透明度"), row,
            Ui.Note("Blue Marble 通常为 100% 不透明，官方蒙版请按网页设置调整。"));
    }
}
