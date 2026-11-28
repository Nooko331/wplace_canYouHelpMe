using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Wplace.Desktop.Controls;

namespace Wplace.Desktop;

internal static class Ui
{
    internal static IBrush Navy = Brush.Parse("#183456"), Muted = Brush.Parse("#536F91"), Blue = Brush.Parse("#086BFA"), Line = Brush.Parse("#C1D2E8"), Pale = Brush.Parse("#EDF4FE");
    internal static TextBlock Text(string text = "", double size = 14, bool bold = false) => new()
    { Text = text, FontFamily = "Microsoft YaHei UI", FontSize = size, Foreground = Navy, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5, VerticalAlignment = VerticalAlignment.Center };
    internal static TextBlock Note(string text = "", double size = 12) { var t = Text(text, size); t.Foreground = Muted; return t; }
    internal static StackPanel Row(params Control[] children) { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in children) row.Children.Add(c); return row; }
    internal static StackPanel Column(params Control[] children) { var col = new StackPanel { Spacing = 8 }; foreach (var c in children) col.Children.Add(c); return col; }
    internal static Border Rule() => new() { Height = 1, Background = Line, Margin = new Thickness(0, 6) };
    internal static PixelIcon Icon(string kind, double size = 24) => new() { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
    internal static Button Button(string text, Action action, string? icon = null, bool primary = false, string? id = null)
    {
        var b = new Button { Classes = { "pixel", "text" }, Content = icon == null ? text : Row(Icon(icon, 21), Text(text, 14, true)) };
        if (primary) b.Classes.Add("selected");
        if (b.Content is StackPanel row) foreach (var label in row.Children.OfType<TextBlock>()) label.Bind(TextBlock.ForegroundProperty, b.GetObservable(Avalonia.Controls.Button.ForegroundProperty));
        b.Click += (_, _) => action();
        AutomationProperties.SetName(b, text);
        if (id != null) { b.Name = id; AutomationProperties.SetAutomationId(b, id); }
        return b;
    }
    internal static Button IconButton(string kind, string label, Action action, string id)
    {
        var b = new Button { Name = id, Content = Icon(kind), Classes = { "pixel" } };
        AutomationProperties.SetName(b, label); AutomationProperties.SetAutomationId(b, id);
        b.Click += (_, _) => action(); return b;
    }
    internal static Border Hint(Control target, string title, Func<string> description)
    {
        var detail = Note(description()); detail.MaxWidth = 280;
        var content = new Border { Background = Brush.Parse("#FCFEFF"), BorderBrush = Navy, BorderThickness = new Thickness(1), Padding = new Thickness(13, 10), Child = Column(Text(title, 14, true), detail) };
        var wrapper = new Border { Child = target, Background = Brushes.Transparent };
        ToolTip.SetTip(wrapper, content); ToolTip.SetShowDelay(wrapper, 350); ToolTip.SetBetweenShowDelay(wrapper, 80); ToolTip.SetPlacement(wrapper, PlacementMode.Bottom);
        wrapper.PointerEntered += (_, _) => detail.Text = description();
        wrapper.AddHandler(InputElement.GotFocusEvent, (_, e) =>
        {
            if (e.NavigationMethod != NavigationMethod.Tab) return;
            detail.Text = description(); ToolTip.SetIsOpen(wrapper, true);
        });
        wrapper.AddHandler(InputElement.LostFocusEvent, (_, _) => ToolTip.SetIsOpen(wrapper, false));
        wrapper.PointerPressed += (_, _) => ToolTip.SetIsOpen(wrapper, false);
        return wrapper;
    }
    internal static Border Key(string text) => new() { BorderBrush = Brush.Parse("#8CA2BE"), BorderThickness = new Thickness(1), Background = Brushes.White, Padding = new Thickness(8, 2), MinWidth = 30, Child = Text(text, 13, true) };
    internal static Border TitleBar(Control content, Window window)
    {
        // A null panel background does not hit-test its empty area. Include the
        // padding in a transparent surface so the entire title bar can be held.
        var bar = new Border { Background = Brushes.Transparent, Padding = new Thickness(20, 14), Child = content };
        EnableDrag(bar, window);
        return bar;
    }
    internal static void EnableDrag(Control surface, Window window, bool capturePointer = false)
    {
        IPointer? dragging = null;
        PixelPoint pointerStart = default, windowStart = default;
        surface.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual source)
            {
                var path = source.GetVisualAncestors().Prepend(source).OfType<Control>().ToArray();
                if (path.Any(c => c is Avalonia.Controls.Button or TextBox or ScrollBar or Slider)) return;
                foreach (var control in path) ToolTip.SetIsOpen(control, false);
            }
            // Handle movement before child controls capture the pointer. The
            // grip is a drag surface, not a Button with competing click logic.
            e.Handled = true;
            if (!capturePointer) { window.BeginMoveDrag(e); return; }
            // The compact floating toolbar tracks the captured pointer directly.
            // Use screen pixels so moving the window never feeds its own offset
            // back into the drag, including on displays with different scaling.
            pointerStart = window.PointToScreen(e.GetPosition(window));
            windowStart = window.Position;
            dragging = e.Pointer;
            e.Pointer.Capture(surface);
        }, RoutingStrategies.Tunnel);
        if (!capturePointer) return;
        void FinishDrag()
        {
            var pointer = dragging;
            dragging = null;
            if (pointer?.Captured == surface) pointer.Capture(null);
        }
        surface.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (dragging != e.Pointer) return;
            if (!e.GetCurrentPoint(surface).Properties.IsLeftButtonPressed) { FinishDrag(); return; }
            var pointer = window.PointToScreen(e.GetPosition(window));
            window.Position = new PixelPoint(windowStart.X + pointer.X - pointerStart.X, windowStart.Y + pointer.Y - pointerStart.Y);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        surface.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (dragging != e.Pointer || e.InitialPressMouseButton != MouseButton.Left) return;
            FinishDrag(); e.Handled = true;
        }, RoutingStrategies.Tunnel);
        surface.PointerCaptureLost += (_, _) => dragging = null;
        window.Deactivated += (_, _) => FinishDrag();
        window.Closed += (_, _) => FinishDrag();
    }
    internal static string Rgb(string? hex)
    {
        if (hex == null) return "尚未记录 · 将鼠标停在画布上，按 A 取色";
        var c = Color.Parse(hex); return $"{hex} · RGB {c.R}, {c.G}, {c.B}";
    }
}

internal sealed class NumberField : Border
{
    private readonly TextBox input;
    private readonly int minimum;
    private readonly string suffix;
    private int value;
    internal int Maximum { get; set; } = int.MaxValue;
    internal int Value => value;
    internal event Action<int>? Committed;
    internal NumberField(string id, string name, int initial, double width = 100, int minimum = 1, string suffix = "")
    {
        Name = id; Width = width; Height = 40; BorderBrush = Brush.Parse("#8198B7"); BorderThickness = new Thickness(1); Background = Brushes.White;
        this.minimum = minimum;
        this.suffix = suffix;
        value = initial;
        input = new TextBox { Name = id + "Input", Text = initial + suffix, Classes = { "step" }, MaxLength = 10 };
        AutomationProperties.SetName(input, name); AutomationProperties.SetAutomationId(input, id + "Input");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,22"), Margin = new Thickness(1) };
        grid.Children.Add(input);
        var up = Ui.IconButton("up", "增加" + name, () => Change(1), id + "Up"); up.Classes.Add("mini"); up.Height = 18;
        var down = Ui.IconButton("down", "减少" + name, () => Change(-1), id + "Down"); down.Classes.Add("mini"); down.Height = 18;
        var arrows = new StackPanel(); arrows.Children.Add(up); arrows.Children.Add(down); Grid.SetColumn(arrows, 1); grid.Children.Add(arrows); Child = grid;
        input.LostFocus += (_, _) => Commit();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            if (e.Key is Key.Up or Key.Down) { Change(e.Key == Key.Up ? 1 : -1); e.Handled = true; }
        };
    }
    internal bool Commit()
    {
        var text = input.Text?.Trim();
        if (suffix == "%" && (text?.EndsWith('%') == true || text?.EndsWith('％') == true))
            text = text.Substring(0, text.Length - 1);
        if (!int.TryParse(text, out var n) || n < minimum || n > Maximum)
        {
            input.Text = value + suffix;
            ToolTip.SetTip(input, $"请输入 {minimum}–{Maximum} 的整数，已恢复为 {value}。");
            ToolTip.SetIsOpen(input, true); return false;
        }
        input.Text = n + suffix;
        if (n == value) return true;
        value = n; Committed?.Invoke(value); return true;
    }
    private void Change(int delta)
    {
        if (!Commit()) return;
        value = (int)Math.Clamp((long)value + delta, minimum, Maximum); input.Text = value + suffix; Committed?.Invoke(value);
    }
    internal void UpdateValue(int n, bool force = false)
    {
        if (!force && input.IsKeyboardFocusWithin) return;
        value = n;
        var text = n + suffix; if (input.Text != text) input.Text = text;
    }
}

internal sealed class PixelProgress : Border
{
    private readonly Grid track = new() { ClipToBounds = true };
    private readonly Border fill = new() { Background = Ui.Blue, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock normal = new() { FontFamily = "Consolas", FontSize = 15, Foreground = Ui.Navy, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock inverse = new() { FontFamily = "Consolas", FontSize = 15, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private int percent;
    internal PixelProgress(double width = double.NaN)
    {
        Width = width; Height = 25; Padding = new Thickness(2); BorderBrush = Brush.Parse("#829AB9"); BorderThickness = new Thickness(1); Background = Ui.Pale;
        track.Children.Add(fill); track.Children.Add(normal); track.Children.Add(inverse); Child = track;
        track.SizeChanged += (_, _) => Update(percent);
    }
    internal void Update(int p)
    {
        percent = Math.Clamp(p, 0, 100); normal.Text = inverse.Text = percent + "%";
        fill.Width = track.Bounds.Width * percent / 100;
        inverse.Clip = new RectangleGeometry(new Rect(0, 0, fill.Width, Math.Max(0, track.Bounds.Height)));
    }
}

internal class PixelWindow : Window
{
    protected readonly StackPanel Body = new() { Spacing = 16 };
    protected readonly Border Footer = new() { IsVisible = false, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(22, 12) };
    protected readonly Grid Header = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
    internal PixelWindow(string title, double width = 680, string icon = "palette")
    {
        Title = title + " · wplace"; Width = width; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowDecorations = WindowDecorations.None; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White; Topmost = true;
        Header.Children.Add(Ui.Row(Ui.Icon(icon, 26), Ui.Text(title, 21, true)));
        var close = Ui.IconButton("close", "关闭", Close, "CloseDialog"); close.Classes.Add("close"); Grid.SetColumn(close, 1); Header.Children.Add(close);
        var layout = new StackPanel(); layout.Children.Add(Ui.TitleBar(Header, this)); layout.Children.Add(Ui.Rule());
        var scroll = new ScrollViewer { Content = new Border { Padding = new Thickness(22, 10, 22, 22), Child = Body }, MaxHeight = 620, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.Children.Add(scroll); layout.Children.Add(Footer);
        Content = new Border { BorderBrush = Ui.Navy, BorderThickness = new Thickness(1), Background = Brushes.White, Child = layout };
        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) is { } screen)
                scroll.MaxHeight = Math.Max(180, Math.Min(620, screen.WorkingArea.Height / screen.Scaling - (Footer.IsVisible ? 200 : 130)));
        };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { OnEscape(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    protected virtual void OnEscape() => Close();
}
