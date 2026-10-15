using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Text.Json;

namespace Wplace.ToolbarPrototype;

public partial class MainWindow : Window
{
    public ToolbarState State { get; } = new();
    private readonly List<(Control Target, TextBlock Body, Func<string> Text)> hints = [];
    private readonly LayoutTransformControl zoomHost;
    private Window? paletteWindow, inspectorWindow, rangeWindow;
    private double previewZoom = 1;
    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        var originalContent = (Control)Content!;
        Content = null;
        zoomHost = new LayoutTransformControl { Child = originalContent };
        Content = zoomHost;
        DataContext = State;
        C<Button>("DragGrip").PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        C<Button>("HoverSwatch").Click += (_, _) => State.CycleSwatches();
        C<Button>("BaseSwatch").Click += (_, _) => State.CycleSwatches();
        C<Button>("PaletteButton").Click += (_, _) => OpenPalette();
        C<Button>("RangeButton").Click += (_, _) => OpenRange();
        C<Button>("EyeButton").Click += (_, _) => State.ShowRange = !State.ShowRange;
        C<Button>("StepUp").Click += (_, _) => ChangeStep(1);
        C<Button>("StepDown").Click += (_, _) => ChangeStep(-1);
        C<TextBox>("StepInput").LostFocus += (_, _) => CommitStep();
        C<TextBox>("StepInput").KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitStep(); e.Handled = true; }
            if (e.Key is Key.Up or Key.Down) { ChangeStep(e.Key == Key.Up ? 1 : -1); e.Handled = true; }
        };
        C<Button>("BalancedButton").Click += (_, _) => State.Extreme = false;
        C<Button>("ExtremeButton").Click += (_, _) => State.Extreme = true;
        C<Button>("RunButton").Click += (_, _) => { CommitStep(); State.Start(); };
        C<Button>("MissingButton").Click += (_, _) => { CommitStep(); State.Start(missing: true); };
        C<Button>("ExpandButton").Click += (_, _) => OpenInspector();
        C<Button>("MinimizeButton").Click += (_, _) => WindowState = WindowState.Minimized;
        C<Button>("CloseButton").Click += (_, _) => Close();
        AddHandler(KeyDownEvent, OnKeyDownAnywhere, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            State.Dispose();
            paletteWindow?.Close(); inspectorWindow?.Close(); rangeWindow?.Close();
        };
        State.PropertyChanged += (_, _) => RefreshVisuals();
        C<Grid>("ProgressTrack").SizeChanged += (_, _) => RefreshVisuals();

        AddHint(C<Button>("DragGrip"), "移动工具栏", () => "按住这里拖动窗口。双击不改变布局。");
        AddHint(C<Border>("HoverTip"), "悬停颜色", () => Rgb(State.HoverColor) + EditHint("取色时鼠标下的原始颜色。单击切换示例。"));
        AddHint(C<Border>("BaseTip"), "移开后底色", () => Rgb(State.BaseColor) + EditHint("移开鼠标后记录的原始颜色。单击切换示例。"));
        AddHint(C<Border>("PaletteTip"), "颜色管理", () => $"想填 {State.Wanted.Count} 色 · 排除 {State.Excluded.Count} 色" + EditHint("设置想填与排除的颜色。本样板使用示例色板。"));
        AddHint(C<Border>("RangeTip"), "划取检测范围", () => State.RangeDescription + EditHint("打开窗内演示画布，拖动选择矩形。"));
        AddHint(C<Button>("EyeButton"), "显示范围与扫描点", () => (State.ShowRange ? "当前已开启。" : "当前已关闭。") + "\n在范围演示画布中查看点阵变化。");
        AddHint(C<Border>("StepTip"), "扫描步长", () => $"当前 {State.Step} px · 范围 1–64 px" + EditHint("输入数字，或使用上下箭头调整。采样点会随步长变化。"));
        AddHint(C<Border>("BalancedTip"), "平衡速度", () => (State.Extreme ? "当前未选中。" : "当前已选中。") + EditHint("较保守的填涂节奏。样板用 30 秒演示进度。"));
        AddHint(C<Border>("ExtremeTip"), "极致速度", () => (State.Extreme ? "当前已选中。" : "当前未选中。") + EditHint("更快的填涂节奏。样板用 15 秒演示进度。"));
        AddHint(C<Border>("RunTip"), "开始自动填涂", () => State.Busy ? "演示正在运行。按 Esc 停止当前演示。" : "按当前参数演示扫描与填涂进度。\n本样板不操作外部画布。Esc 停止。");
        AddHint(C<Border>("MissingTip"), "检测遗漏", () => State.Busy ? "演示正在运行。按 Esc 停止后再检测。" : "演示遗漏检测与补涂的进度状态。\n本样板不读取屏幕或执行涂色。");
        AddHint(C<Border>("ProgressTip"), "任务进度", () => State.Status + $"\n当前 {State.Percent} · 数字区域固定宽度。");
        AddHint(C<Border>("TimeTip"), "预计剩余时间", () => State.Status + $"\n剩余 {State.Remaining} · 尚不能估算时显示 --:--。");
        AddHint(C<Button>("ExpandButton"), "完整布局", () => "本次只实现精简工具栏样板。\n单击打开预览面板，调整进度与预览倍率。");
        AddHint(C<Button>("MinimizeButton"), "最小化", () => "收起到任务栏。");
        AddHint(C<Button>("CloseButton"), "关闭样板", () => "结束当前演示并关闭样板窗口。");
        foreach (var h in hints)
        {
            var focusControls = h.Target.GetVisualDescendants().OfType<InputElement>().ToList();
            if (h.Target is InputElement input) focusControls.Add(input);
            foreach (var focus in focusControls)
            {
                focus.GotFocus += (_, e) => { if (e.NavigationMethod == NavigationMethod.Tab) ToolTip.SetIsOpen(h.Target, true); };
                focus.LostFocus += (_, _) => ToolTip.SetIsOpen(h.Target, false);
            }
        }
        Opened += (_, _) =>
        {
            RefreshVisuals();
            var args = Program.Arguments;
            var captureIndex = Array.IndexOf(args, "--capture");
            if (captureIndex >= 0 && captureIndex + 1 < args.Length)
                DispatcherTimer.RunOnce(() => CaptureReview(args[captureIndex + 1], args.Contains("--exit-after-capture")), TimeSpan.FromMilliseconds(700));
        };
        RefreshVisuals();
    }

    private string EditHint(string body) => State.Busy ? "\n任务运行中，按 Esc 停止后可调整。" : "\n" + body;
    private static string Rgb(string hex)
    {
        var c = Color.Parse(hex); return $"{hex} · RGB {c.R}, {c.G}, {c.B}";
    }
    private void OnKeyDownAnywhere(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        foreach (var h in hints) ToolTip.SetIsOpen(h.Target, false);
        State.Stop(); e.Handled = true;
    }
    private void CommitStep()
    {
        var input = C<TextBox>("StepInput");
        if (int.TryParse(input.Text, out var number)) State.Step = number;
        input.Text = State.Step.ToString();
    }
    private void ChangeStep(int delta) { CommitStep(); State.Step += delta; C<TextBox>("StepInput").Text = State.Step.ToString(); }
    private void RefreshVisuals()
    {
        C<Button>("EyeButton").Classes.Set("selected", State.ShowRange);
        C<Button>("BalancedButton").Classes.Set("selected", !State.Extreme);
        C<Button>("ExtremeButton").Classes.Set("selected", State.Extreme);
        C<Border>("HoverFill").Background = Brush.Parse(State.HoverColor);
        C<Border>("BaseFill").Background = Brush.Parse(State.BaseColor);
        var track = C<Grid>("ProgressTrack");
        var fillWidth = Math.Max(0, track.Bounds.Width * State.Progress / 100);
        C<Border>("ProgressFill").Width = fillWidth;
        C<TextBlock>("PercentOnFillText").Clip = new RectangleGeometry(new Rect(0, 0, fillWidth, Math.Max(0, track.Bounds.Height)));
        foreach (var h in hints) h.Body.Text = h.Text();
    }
    private void AddHint(Control target, string title, Func<string> body)
    {
        var detail = new TextBlock { Text = body(), TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#536F91"), FontSize = 12, LineHeight = 21, MaxWidth = 278 };
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14, Foreground = Brush.Parse("#183456") });
        stack.Children.Add(detail);
        var card = new Border { Background = Brush.Parse("#FCFEFF"), BorderBrush = Brush.Parse("#24456D"), BorderThickness = new Thickness(1), Padding = new Thickness(14,11), Child = stack, BoxShadow = BoxShadows.Parse("2 2 0 0 #ADC0DC") };
        ToolTip.SetTip(target, card);
        ToolTip.SetPlacement(target, PlacementMode.Bottom);
        ToolTip.SetShowDelay(target, 350);
        ToolTip.SetBetweenShowDelay(target, 80);
        target.PointerPressed += (_, _) => ToolTip.SetIsOpen(target, false);
        hints.Add((target, detail, body));
    }
    private Window CreateSheet(string title, Control body, double width = 500)
    {
        var window = new Window
        {
            Title = title + " · Avalonia 样板", Width = width, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowDecorations = Avalonia.Controls.WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        var close = MakeButton("关闭", (_, _) => window.Close());
        close.Width = 64; close.MinWidth = 64; close.Height = 32;
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16,12) };
        head.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1); head.Children.Add(close);
        head.PointerPressed += (_, e) =>
        {
            var source = e.Source as Visual;
            if (source is Button || source?.GetVisualAncestors().OfType<Button>().Any() == true) return;
            if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) window.BeginMoveDrag(e);
        };
        var content = new StackPanel();
        content.Children.Add(head);
        content.Children.Add(new Border { Height = 1, Background = Brush.Parse("#B5C9E3") });
        content.Children.Add(new Border { Padding = new Thickness(18), Child = body });
        window.Content = new Border { BorderBrush = Brush.Parse("#24456D"), BorderThickness = new Thickness(1), Background = Brush.Parse("#FAFCFF"), Child = content };
        window.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            State.Stop(); window.Close(); e.Handled = true;
        }, RoutingStrategies.Tunnel);
        return window;
    }
    private static Button MakeButton(string text, EventHandler<RoutedEventArgs> action)
    {
        var button = new Button { Content = text, Classes = { "pixel", "text" } };
        button.Click += action; return button;
    }
    private static TextBlock Label(string text, double size = 13) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.7, Foreground = Brush.Parse("#536F91") };

    private void OpenPalette()
    {
        if (paletteWindow is not null) { paletteWindow.Activate(); return; }
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(Label("颜色规则预览 · 16 种示例色。修改仅用于这个样板。"));
        var summary = Label("");
        var list = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = 48, ItemHeight = 48 };
        bool exclude = false;
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button wanted = null!, excluded = null!;
        var colorButtons = new List<(Button Button, string Color)>();
        void Refresh()
        {
            wanted.Classes.Set("selected", !exclude); excluded.Classes.Set("selected", exclude);
            foreach (var item in colorButtons)
            {
                var selected = (exclude ? State.Excluded : State.Wanted).Contains(item.Color);
                item.Button.Classes.Set("selected", selected);
                ToolTip.SetTip(item.Button, item.Color + (selected ? " · 已选，单击移除" : " · 单击加入"));
            }
            summary.Text = $"想填 {State.Wanted.Count} 色 · 排除 {State.Excluded.Count} 色";
            State.NotifyColorsChanged();
        }
        wanted = MakeButton("我想填的颜色", (_, _) => { exclude = false; Refresh(); });
        excluded = MakeButton("我不想填的颜色", (_, _) => { exclude = true; Refresh(); });
        tabs.Children.Add(wanted); tabs.Children.Add(excluded); body.Children.Add(tabs);
        foreach (var hex in ToolbarState.Palette)
        {
            var button = new Button { Width = 43, Height = 43, Padding = new Thickness(5), Classes = { "pixel" }, Content = new Border { Width = 27, Height = 27, Background = Brush.Parse(hex) } };
            AutomationProperties.SetName(button, "示例颜色 " + hex);
            button.Click += (_, _) =>
            {
                var target = exclude ? State.Excluded : State.Wanted;
                var other = exclude ? State.Wanted : State.Excluded;
                if (!target.Remove(hex)) { other.Remove(hex); target.Add(hex); }
                Refresh();
            };
            colorButtons.Add((button, hex)); list.Children.Add(button);
        }
        body.Children.Add(list); body.Children.Add(summary);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(MakeButton("全部示例色", (_, _) => { State.Wanted.UnionWith(ToolbarState.Palette); State.Excluded.Clear(); Refresh(); }));
        actions.Children.Add(MakeButton("清空想填", (_, _) => { State.Wanted.Clear(); Refresh(); }));
        body.Children.Add(actions);
        body.Children.Add(Label("未指定想填颜色时，默认使用全部颜色，再减去排除项。", 12));
        Refresh();
        paletteWindow = CreateSheet("颜色管理", body, 444);
        paletteWindow.Closed += (_, _) => paletteWindow = null;
        _ = paletteWindow.ShowDialog(this);
    }
    private void OpenRange()
    {
        if (rangeWindow is not null) { rangeWindow.Activate(); return; }
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Label("在下面的演示画布内按住左键拖动，松开确认。Esc 取消。\n这是矩形交互样板，不读取或操作桌面。"));
        var canvas = new RangePreview(State) { Width = 620, Height = 280 };
        canvas.RangeAccepted += (_, rect) =>
        {
            State.SetRange(rect.Width, rect.Height);
            rangeWindow?.Close();
        };
        body.Children.Add(canvas);
        body.Children.Add(Label($"扫描步长 {State.Step} px · " + (State.ShowRange ? "扫描点已显示" : "扫描点已隐藏"), 12));
        rangeWindow = CreateSheet("划取检测范围 · 窗内演示", body, 658);
        rangeWindow.Closed += (_, _) => rangeWindow = null;
        _ = rangeWindow.ShowDialog(this);
    }
    private void OpenInspector()
    {
        if (inspectorWindow is not null) { inspectorWindow.Activate(); return; }
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(Label("Avalonia 12.1.4 · .NET 10", 15));
        body.Children.Add(Label("此版本只验证精简工具栏的视觉与交互。取色、扫描、填涂均使用模拟数据。完整布局与功能介绍尚未迁移。"));
        var scaleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var scale in new[] { 1.0, 1.25, 1.5 })
        {
            var b = MakeButton($"{scale * 100:0}%", (_, _) =>
            {
                previewZoom = scale; zoomHost.LayoutTransform = new ScaleTransform(scale, scale);
            });
            b.MinWidth = 70; scaleRow.Children.Add(b);
        }
        body.Children.Add(Label("预览倍率（不修改系统 DPI）")); body.Children.Add(scaleRow);
        var previewRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var p in new[] { 0, 9, 62, 100 })
        {
            var b = MakeButton(p + "%", (_, _) => State.SetPreview(p, p == 100 ? "01:23:45" : "00:42"));
            b.MinWidth = 70; previewRow.Children.Add(b);
        }
        body.Children.Add(Label("固定宽度检查 · 100% 同时显示长时间示例")); body.Children.Add(previewRow);
        var stop = MakeButton("停止演示 · Esc", (_, _) => State.Stop());
        body.Children.Add(stop);
        body.Children.Add(Label("支持 Tab / Shift+Tab 导航、上下键调整步长。Esc 在样板窗口获得焦点时停止演示。", 12));
        inspectorWindow = CreateSheet("工具栏样板 · 预览面板", body, 470);
        inspectorWindow.Closed += (_, _) => inspectorWindow = null;
        inspectorWindow.Show(this);
    }
    internal void CaptureReview(string directory, bool exit)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var frame = C<Border>("ToolbarFrame");
            var samples = new List<object>();
            foreach (var value in new[] { 0, 9, 62, 100 })
            {
                State.SetPreview(value, value == 100 ? "01:23:45" : "00:42");
                Dispatcher.UIThread.RunJobs();
                RefreshVisuals();
                foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                {
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(frame.Bounds.Width * scale), (int)Math.Ceiling(frame.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
                    bitmap.Render(frame);
                    bitmap.Save(Path.Combine(directory, $"toolbar-{value:D3}-{scale * 100:0}.png"), PngBitmapEncoderOptions.Default);
                }
                object Box(string name)
                {
                    var c = C<Control>(name);
                    var point = c.TranslatePoint(default, frame);
                    return new { X = point?.X, Y = point?.Y, Width = c.Bounds.Width, Height = c.Bounds.Height };
                }
                samples.Add(new { Percent = value, FrameWidth = frame.Bounds.Width, FrameHeight = frame.Bounds.Height, Progress = Box("ProgressTip"), Time = Box("TimeTip"), Expand = Box("ExpandButton"), Close = Box("CloseButton") });
            }
            var tip = (Control)ToolTip.GetTip(C<Border>("PaletteTip"))!;
            tip.Measure(new Size(320, double.PositiveInfinity));
            tip.Arrange(new Rect(tip.DesiredSize));
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(tip.Bounds.Width * 1.5), (int)Math.Ceiling(tip.Bounds.Height * 1.5)), new Vector(144, 144)))
            { bitmap.Render(tip); bitmap.Save(Path.Combine(directory, "tooltip-150.png"), PngBitmapEncoderOptions.Default); }
            File.WriteAllText(Path.Combine(directory, "layout-check.json"), JsonSerializer.Serialize(new { Framework = "Avalonia 12.1.4 / .NET 10", RenderScaling, PreviewZoom = previewZoom, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
            State.SetPreview(62);
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(directory, "capture-error.txt"), error.ToString()); }
        if (exit) Close();
    }
}
