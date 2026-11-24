using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WplaceColorWatch;

namespace Wplace.Desktop;

public sealed partial class MainWindow : Window
{
    internal readonly WindowsEngineHost Engine;
    internal DesktopSnapshot State { get; private set; } = new();
    private readonly List<Control> editable = [];
    private readonly List<Button> balanced = [], extreme = [], eyes = [];
    private readonly List<Border> hoverSwatches = [], clearSwatches = [];
    private readonly PixelProgress compactProgress = new(142), fullProgress = new();
    private readonly TextBlock compactTime = Ui.Text("--:--", 16), fullTime = Ui.Note("预计剩余 --:--"), fullCount = Ui.Text("0 / 0", 14), status = Ui.Text("启动中", 14), rangeText = Ui.Text("尚未划取范围", 14);
    private readonly TextBlock hoverRgb = Ui.Note("尚未记录"), clearRgb = Ui.Note("尚未记录"), effectiveText = Ui.Note("默认使用全部 63 色"), wantedText = Ui.Text(), excludedText = Ui.Text();
    private readonly WrapPanel wantedPreview = new() { Orientation = Orientation.Horizontal }, excludedPreview = new() { Orientation = Orientation.Horizontal };
    private readonly NumberField compactStep = new("CompactStep", "扫描步长", 10, 86), fullStep = new("FullStep", "扫描步长", 10, 115), workers = new("Workers", "CPU 数量", 1, 115);
    private readonly Border frame;
    private readonly Control full, compact;
    private readonly List<Window> children = [];
    private Button? guideButton;
    private Button? updateLink;
    private ScrollViewer? fullContent;
    private ColorManagerWindow? colorManager;
    private FeatureGuideWindow? guideWindow;
    private NotesWindow? notesWindow;
    private bool compactMode, initialStateApplied, closing, allowClose, applying, startupHandled;
    private string colorsSignature = "";
    private string updateUrl = ReleaseUpdateChecker.RepositoryUrl;
    internal bool CompactMode => compactMode;
    internal bool GuideEntryVisible => guideButton?.IsEffectivelyVisible == true;

    public MainWindow()
    {
        Title = "wplace 填涂助手 · Avalonia v" + Program.Version + (Program.Preview ? " · 界面预览" : "");
        WindowDecorations = WindowDecorations.None; CanResize = false; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        var settingsPath = Program.Arguments.Contains("--settings-path") ? ReadArg("--settings-path") : null;
        Engine = new WindowsEngineHost(Program.Preview, settingsPath: settingsPath, options: Program.Options);
        AddHandler(GotFocusEvent, (_, e) => Engine.EditingInput = e.Source is TextBox);
        AddHandler(LostFocusEvent, (_, _) => Engine.EditingInput = false);
        full = BuildFull(); compact = new Viewbox { Child = BuildCompact(), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly }; compact.IsVisible = false;
        var root = new Grid(); root.Children.Add(full); root.Children.Add(compact);
        BuildMaskGuide(root);
        frame = new Border { BorderBrush = Ui.Navy, BorderThickness = new Thickness(1), Background = Brushes.White, Child = root, Margin = new Thickness(0, 0, 2, 2) };
        Content = frame;
        workers.Maximum = Math.Max(1, Environment.ProcessorCount - 1);
        workers.Committed += _ => SaveParameters(); fullStep.Committed += _ => SaveParameters(); compactStep.Committed += _ => SaveParameters();
        // Commands resume on the dispatcher before some older timer posts. Read
        // the newest state at delivery time so a queued "colors closed" snapshot
        // cannot dismiss a dialog that was just opened by a newer command.
        Engine.SnapshotChanged += _ => Dispatcher.UIThread.Post(() => ApplyState(Engine.Snapshot));
        Engine.ActivationRequested += ActivateFromOutside;
        Program.ActivateRequested = ActivateFromOutside;
        Engine.RangeSelectionChanged += selecting => Dispatcher.UIThread.Invoke(() =>
        {
            if (selecting) Hide();
            else if (!closing) { Show(); Activate(); }
        });
        Engine.PromptHandler = prompt =>
        {
            var completion = new TaskCompletionSource<EngineAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try { completion.TrySetResult(await ShowPromptAsync(prompt)); }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            return completion.Task;
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && State.Busy) { Run(() => Engine.ExecuteAsync(DesktopCommand.Stop)); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        SizeChanged += (_, _) => FitToWorkingArea();
        ScalingChanged += (_, _) => UpdateScreenConstraints();
        Opened += async (_, _) =>
        {
            try
            {
                UpdateScreenConstraints();
                if (startupHandled) return;
                startupHandled = true;
                await Engine.Ready; ApplyState(Engine.Snapshot);
                if (Program.Arguments.Contains("--capture") || Program.Arguments.Contains("--smoke-test"))
                {
                    await DesktopReview.RunAsync(this, ReadArg("--capture") ?? ReadArg("--smoke-test")!);
                    Close(); return;
                }
                if (Program.Arguments.Contains("--preview-release-notes")) { await ShowNotesAsync(false); Close(); return; }
                if (!Program.Preview)
                {
                    await maskChosen.Task;
                    if (closing) return;
                    await ShowNotesAsync(true);
                    _ = CheckUpdatesAsync();
                }
            }
            catch (Exception error) { await ShowPromptAsync(new EnginePrompt("启动遇到问题", error.Message, false, null)); }
        };
    }

    private Control BuildFull()
    {
        var page = new StackPanel { Width = 900 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(Ui.Row(Ui.Icon("pen", 30), Ui.Text("wplace 填涂助手", 25, true), Ui.Note("v" + Program.Version, 13)));
        var minimize = Ui.IconButton("minimize", "最小化", () => WindowState = WindowState.Minimized, "FullMinimize");
        var close = Ui.IconButton("close", "关闭", Close, "FullClose"); close.Classes.Add("close");
        var controls = Ui.Row(Ui.Button("精简布局", () => SetCompact(true), "compact", id: "CompactLayout"), minimize, close);
        Grid.SetColumn(controls, 1); header.Children.Add(controls); page.Children.Add(Ui.TitleBar(header, this));
        page.Children.Add(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1), Background = Ui.Pale, Padding = new Thickness(20, 10), Child = Ui.Row(Ui.Key("A"), Ui.Text(Program.Preview ? "界面预览 · 未启用全局快捷键与填涂" : "将鼠标停在画布上，按 A 取色", 15, true)) });

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("540,360") };
        var left = new StackPanel { Spacing = 10, Margin = new Thickness(20, 16) };
        left.Children.Add(Section("pen", "取色记录"));
        var leftColor = MakeSwatch(hoverSwatches, 52, 52); var rightColor = MakeSwatch(clearSwatches, 52, 52);
        var colors = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        colors.Children.Add(ColorCell(leftColor, "悬停颜色", hoverRgb));
        var clear = ColorCell(rightColor, "移开后底色", clearRgb); Grid.SetColumn(clear, 1); colors.Children.Add(clear); left.Children.Add(colors);
        left.Children.Add(Ui.Rule());
        var rangeHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; rangeHeading.Children.Add(Section("range", "检测范围"));
        var select = Ui.Button("划取检测范围", () => Command(DesktopCommand.SelectRange), id: "FullSelectRange"); editable.Add(select); Grid.SetColumn(select, 1); rangeHeading.Children.Add(select); left.Children.Add(rangeHeading);
        left.Children.Add(new Border { Padding = new Thickness(12, 9), Background = Ui.Pale, Child = rangeText });
        left.Children.Add(Ui.Note("拖动框选矩形 · 单击绘制正交多边形 · Esc 取消"));
        var eye = Ui.Button("显示范围与扫描点", ToggleRange, "eye", id: "FullShowRange"); eyes.Add(eye); editable.Add(eye); left.Children.Add(eye);
        left.Children.Add(Ui.Rule());
        maskSection = BuildMaskSection(false);
        left.Children.Add(maskSection); left.Children.Add(Ui.Rule());
        var colorHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; colorHeading.Children.Add(Section("palette", "颜色规则"));
        var manage = Ui.Button("颜色管理", OpenColors, id: "FullColors"); editable.Add(manage); Grid.SetColumn(manage, 1); colorHeading.Children.Add(manage); left.Children.Add(colorHeading);
        left.Children.Add(Ui.Column(Ui.Row(wantedText, wantedPreview), Ui.Row(excludedText, excludedPreview)));
        left.Children.Add(effectiveText); columns.Children.Add(left);

        var right = new StackPanel { Spacing = 10, Margin = new Thickness(20, 16) };
        right.Children.Add(Section("cpu", "扫描与填涂"));
        var auto = Ui.Button("自动", () => Command(DesktopCommand.AutoWorkers), id: "AutoWorkers"); auto.MinWidth = 62; editable.Add(auto); editable.Add(workers); editable.Add(fullStep);
        right.Children.Add(Ui.Row(Ui.Text("CPU 数量", 14, true), workers, auto)); right.Children.Add(Ui.Note("仅影响扫描并行度；自动使用约一半逻辑处理器。"));
        right.Children.Add(Ui.Row(Ui.Text("扫描步长", 14, true), fullStep, Ui.Note("px"))); right.Children.Add(Ui.Note("让采样点落在目标像素方块上。步长太大会漏过小区域。"));
        right.Children.Add(Ui.Rule()); right.Children.Add(Ui.Text("填涂速度", 16, true));
        var balance = Ui.Button("平衡", () => SetSpeed(false), "gauge", id: "FullBalanced"); var fast = Ui.Button("极致速度", () => SetSpeed(true), "bolt", id: "FullExtreme");
        balanced.Add(balance); extreme.Add(fast); editable.Add(balance); editable.Add(fast); right.Children.Add(Ui.Row(balance, fast));
        right.Children.Add(Ui.Note("出现遗漏时可切回平衡模式。两种模式都保留取色后首次涂色验证。")); right.Children.Add(Ui.Rule());
        right.Children.Add(Ui.Text("快捷操作", 16, true)); right.Children.Add(Ui.Row(Ui.Key("A"), Ui.Text("取色"))); right.Children.Add(Ui.Row(Ui.Key("Esc"), Ui.Text("停止当前任务")));
        var rightBackground = new Border { Background = Ui.Pale, BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 0, 0), Child = right }; Grid.SetColumn(rightBackground, 1); columns.Children.Add(rightBackground);
        fullContent = new ScrollViewer { Content = columns, MaxHeight = 530, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; page.Children.Add(fullContent);
        var progress = new StackPanel { Spacing = 10, Margin = new Thickness(20, 12) };
        var progressLabels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") }; progressLabels.Children.Add(Ui.Row(Ui.Text("任务进度", 16, true), status));
        fullTime.Margin = new Thickness(16, 0); Grid.SetColumn(fullTime, 1); progressLabels.Children.Add(fullTime); Grid.SetColumn(fullCount, 2); progressLabels.Children.Add(fullCount);
        progress.Children.Add(progressLabels); progress.Children.Add(fullProgress);
        var fill = Ui.Button("开始自动填涂", () => Command(DesktopCommand.StartFill), "play", true, "FullStart"); var missing = Ui.Button("检测遗漏", () => Command(DesktopCommand.DetectMissing), "missing", true, "FullMissing");
        editable.Add(fill); editable.Add(missing); progress.Children.Add(Ui.Row(fill, missing, Ui.Key("Esc"), Ui.Note("随时停止"))); page.Children.Add(progress);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), Margin = new Thickness(20, 8, 20, 14) };
        var notes = FooterLink("更新说明", () => Run(() => ShowNotesAsync(false)), "ReleaseNotes"); footer.Children.Add(notes); editable.Add(notes);
        updateLink = FooterLink("GitHub", () => OpenUrl(updateUrl), "ProjectLink"); Grid.SetColumn(updateLink, 1); footer.Children.Add(updateLink); editable.Add(updateLink);
        guideButton = FooterLink("功能介绍", OpenGuide, "FeatureGuide"); Grid.SetColumn(guideButton, 2); footer.Children.Add(guideButton); editable.Add(guideButton);
        var hint = Ui.Note(Program.Preview ? "界面预览 · 不操作桌面" : "工具窗口请放在检测范围外", 11); hint.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(hint, 3); footer.Children.Add(hint);
        page.Children.Add(Ui.Rule()); page.Children.Add(footer); return page;
    }

    private Control BuildCompact()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 4, Margin = new Thickness(10, 10, 8, 10) };
        var grip = new Border { Name = "DragGrip", Width = 18, Height = 40, Background = Brushes.Transparent, Child = Ui.Icon("grip", 18), Cursor = new Cursor(StandardCursorType.SizeAll) };
        AutomationProperties.SetName(grip, "拖动工具栏"); AutomationProperties.SetAutomationId(grip, "DragGrip");
        row.Children.Add(Ui.Hint(grip, "移动工具栏", () => "按住六点拖动。将工具栏放在检测范围以外。"));
        row.Children.Add(Ui.Hint(MakeSwatch(hoverSwatches, 34, 40), "悬停颜色", () => Ui.Rgb(State.HoverColor)));
        row.Children.Add(Ui.Hint(MakeSwatch(clearSwatches, 34, 40), "移开后底色", () => Ui.Rgb(State.ClearColor)));
        AddCompactButton(row, "palette", "颜色管理", OpenColors, "CompactColors", () => $"想填 {State.Wanted.Count} 色 · 排除 {State.Excluded.Count} 色\n管理颜色规则或进入 A 键连续选色。");
        Divide(row);
        AddCompactButton(row, "range", "划取检测范围", () => Command(DesktopCommand.SelectRange), "CompactRange", () => State.Range + "\n拖动选矩形，单击绘制正交多边形。");
        var eye = AddCompactButton(row, "eye", "显示范围与扫描点", ToggleRange, "CompactEye", () => State.ShowRange ? "当前已显示。点击隐藏红框和采样点。" : "当前已隐藏。点击显示红框和采样点。"); eyes.Add(eye);
        Divide(row);
        editable.Add(compactStep); row.Children.Add(Ui.Hint(Ui.Row(Ui.Icon("step", 22), compactStep), "扫描步长", () => $"当前 {State.Step} px。输入正整数或用上下箭头调整。"));
        Divide(row);
        balanced.Add(AddCompactButton(row, "gauge", "平衡速度", () => SetSpeed(false), "CompactBalanced", () => "较稳妥的填涂节奏。出现遗漏时优先使用。"));
        extreme.Add(AddCompactButton(row, "bolt", "极致速度", () => SetSpeed(true), "CompactExtreme", () => "缩短填涂间隔；仍保留取色后首次验证。"));
        Divide(row);
        var play = AddCompactButton(row, "play", "开始自动填涂", () => Command(DesktopCommand.StartFill), "CompactStart", () => "先扫描当前范围，再按颜色填涂。\n运行时按 Esc 停止。"); play.Classes.Add("selected"); play.Width = 44;
        AddCompactButton(row, "missing", "检测遗漏", () => Command(DesktopCommand.DetectMissing), "CompactMissing", () => "建议完成填涂后使用。发现候选点后会先询问是否补涂。").Classes.Add("selected");
        Divide(row);
        row.Children.Add(Ui.Hint(compactProgress, "任务进度", () => State.Status + " · " + State.Percent + "%"));
        compactTime.FontFamily = "Consolas"; compactTime.HorizontalAlignment = HorizontalAlignment.Center;
        var clock = Ui.Row(Ui.Icon("clock", 22), new Border { Width = 84, Height = 32, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Background = Brushes.White, Child = compactTime });
        clock.Margin = new Thickness(6, 0); row.Children.Add(Ui.Hint(clock, "预计剩余时间", () => "根据当前阶段的处理速度估算；未能估算时显示 --:--。"));
        Divide(row);
        AddCompactButton(row, "expand", "完整布局", () => SetCompact(false), "ExpandLayout", () => "展开后可调整 CPU 数量，并查看更新说明与功能介绍。", false);
        AddCompactButton(row, "minimize", "最小化", () => WindowState = WindowState.Minimized, "CompactMinimize", () => "收起到任务栏。", false);
        var close = AddCompactButton(row, "close", "关闭程序", Close, "CompactClose", () => "停止当前任务并退出。", false); close.Classes.Add("close");
        var toolbar = new Border { BorderBrush = Brush.Parse("#DCE8F7"), BorderThickness = new Thickness(1), Margin = new Thickness(2), Background = Brush.Parse("#FBFDFF"), Child = row };
        Ui.EnableDrag(toolbar, this, capturePointer: true);
        return toolbar;
    }

    private Button AddCompactButton(StackPanel row, string kind, string label, Action action, string id, Func<string> tip, bool edit = true)
    {
        var b = Ui.IconButton(kind, label, action, id); if (edit) editable.Add(b); row.Children.Add(Ui.Hint(b, label, () => State.Busy && edit ? "任务运行中，按 Esc 停止后可操作。" : tip())); return b;
    }
    private static void Divide(StackPanel row) => row.Children.Add(new Border { Width = 1, Height = 28, Background = Ui.Line, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center });
    private static StackPanel Section(string icon, string text) => Ui.Row(Ui.Icon(icon, 27), Ui.Text(text, 20, true));
    private static Grid ColorCell(Control swatch, string title, TextBlock detail)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("52,*"), Margin = new Thickness(0, 0, 12, 0) };
        grid.Children.Add(swatch);
        var text = Ui.Column(Ui.Text(title, 14, true), detail); text.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(text, 1); grid.Children.Add(text);
        return grid;
    }
    private static Border MakeSwatch(List<Border> list, double w, double h)
    {
        var inner = new Border { Background = Ui.Pale, BorderBrush = Brush.Parse("#D9E4F4"), BorderThickness = new Thickness(2), Margin = new Thickness(2) }; list.Add(inner);
        return new Border { Width = w, Height = h, Child = inner, Background = Brushes.White, BorderBrush = Brush.Parse("#8198B7"), BorderThickness = new Thickness(1) };
    }
    private static Button FooterLink(string text, Action action, string id)
    {
        var b = Ui.Button(text, action, id: id); b.MinWidth = 0; b.Padding = new Thickness(10, 2); b.Height = 31; b.Margin = new Thickness(0, 0, 8, 0); return b;
    }

    internal void ApplyState(DesktopSnapshot snapshot)
    {
        if (closing) return;
        State = snapshot; applying = true;
        try
        {
            if (!initialStateApplied && snapshot.Ready) { initialStateApplied = true; SetCompact(snapshot.Compact, false); }
            foreach (var c in editable) c.IsEnabled = snapshot.Ready && !snapshot.Busy;
            UpdateMaskGuide(snapshot);
            foreach (var b in balanced) b.Classes.Set("selected", !snapshot.Extreme);
            foreach (var b in extreme) b.Classes.Set("selected", snapshot.Extreme);
            foreach (var b in eyes) b.Classes.Set("selected", snapshot.ShowRange);
            workers.UpdateValue(snapshot.Workers); fullStep.UpdateValue(snapshot.Step); compactStep.UpdateValue(snapshot.Step);
            foreach (var c in hoverSwatches) c.Background = Brush.Parse(snapshot.HoverColor ?? "#EDF4FE");
            foreach (var c in clearSwatches) c.Background = Brush.Parse(snapshot.ClearColor ?? "#EDF4FE");
            hoverRgb.Text = Ui.Rgb(snapshot.HoverColor); clearRgb.Text = Ui.Rgb(snapshot.ClearColor);
            rangeText.Text = snapshot.Range; status.Text = snapshot.Status;
            compactProgress.Update(snapshot.Percent); fullProgress.Update(snapshot.Percent);
            compactTime.Text = snapshot.Remaining; fullTime.Text = "预计剩余 " + snapshot.Remaining; fullCount.Text = $"{snapshot.Current:N0} / {snapshot.Total:N0}";
            string signature = string.Join(',', snapshot.Wanted) + "|" + string.Join(',', snapshot.Excluded);
            if (colorsSignature != signature)
            {
                colorsSignature = signature;
                wantedText.Text = $"想填 {snapshot.Wanted.Count} 色"; excludedText.Text = $"排除 {snapshot.Excluded.Count} 色";
                FillPreview(wantedPreview, snapshot.Wanted); FillPreview(excludedPreview, snapshot.Excluded);
            }
            effectiveText.Text = (snapshot.Wanted.Count == 0 ? "未指定想填，默认全部颜色" : "已按想填列表筛选") + $" · 当前可填 {snapshot.EffectiveCount} 色";
            colorManager?.UpdateState(snapshot);
            if (colorManager != null && !snapshot.ColorsOpen) colorManager.Close();
        }
        finally { applying = false; }
    }
    private static void FillPreview(WrapPanel panel, IReadOnlyList<string> colors)
    {
        panel.Children.Clear(); foreach (var hex in colors.Take(10)) panel.Children.Add(new Border { Width = 20, Height = 20, Margin = new Thickness(2), Background = Brush.Parse(hex), BorderBrush = Ui.Muted, BorderThickness = new Thickness(1) });
        if (colors.Count > 10) panel.Children.Add(Ui.Note("+" + (colors.Count - 10)));
    }
    internal void SetCompact(bool value, bool syncEngine = true)
    {
        compactMode = value; full.IsVisible = !value; compact.IsVisible = value;
        if (value) guideWindow?.Close();
        if (syncEngine) Run(() => Engine.SetCompactAsync(value));
        UpdateScreenConstraints();
        InvalidateMeasure();
    }
    private void UpdateScreenConstraints()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        compact.MaxWidth = Math.Max(500, screen.WorkingArea.Width / screen.Scaling - 12);
        if (fullContent == null) return;
        double chrome = fullContent.Bounds.Height > 0 && full.Bounds.Height > 0 ? full.Bounds.Height - fullContent.Bounds.Height : 330;
        fullContent.MaxHeight = Math.Max(180, Math.Min(640, screen.WorkingArea.Height / screen.Scaling - chrome - 16));
    }
    private void FitToWorkingArea()
    {
        if (!IsVisible || Screens.ScreenFromWindow(this) is not { } screen) return;
        var work = screen.WorkingArea;
        int width = (int)Math.Ceiling(Bounds.Width * RenderScaling), height = (int)Math.Ceiling(Bounds.Height * RenderScaling);
        var fit = new PixelPoint(Math.Clamp(Position.X, work.X, Math.Max(work.X, work.Right - width)), Math.Clamp(Position.Y, work.Y, Math.Max(work.Y, work.Bottom - height)));
        if (fit != Position) Position = fit;
    }
    private void SaveParameters() { if (!applying && initialStateApplied) Run(() => Engine.SetParametersAsync(workers.Value, compactMode ? compactStep.Value : fullStep.Value, State.ShowRange, State.Extreme)); }
    private void SetSpeed(bool fast) => Run(() => Engine.SetParametersAsync(workers.Value, compactMode ? compactStep.Value : fullStep.Value, State.ShowRange, fast));
    private void ToggleRange() => Run(() => Engine.SetParametersAsync(workers.Value, compactMode ? compactStep.Value : fullStep.Value, !State.ShowRange, State.Extreme));
    private void Command(DesktopCommand command) => Run(async () =>
    {
        if (!workers.Commit() || !(compactMode ? compactStep : fullStep).Commit()) return;
        await Engine.SetParametersAsync(workers.Value, compactMode ? compactStep.Value : fullStep.Value, State.ShowRange, State.Extreme);
        await Engine.ExecuteAsync(command);
    });
    internal void OpenColors() => Run(async () =>
    {
        if (colorManager != null) { colorManager.Activate(); return; }
        await Engine.ExecuteAsync(DesktopCommand.OpenColors);
        ApplyState(Engine.Snapshot);
        colorManager = new ColorManagerWindow(this); RegisterChild(colorManager);
        colorManager.Closed += async (_, _) => { colorManager = null; if (!closing) await Engine.ExecuteAsync(DesktopCommand.CloseColors); };
        colorManager.UpdateState(Engine.Snapshot);
        _ = colorManager.ShowDialog(this);
    });
    internal void OpenGuide()
    {
        if (compactMode || State.Busy) return;
        if (guideWindow != null) { guideWindow.Activate(); return; }
        guideWindow = new FeatureGuideWindow(); RegisterChild(guideWindow);
        guideWindow.Closed += (_, _) => { guideWindow = null; UpdateHotkeySuppression(); };
        UpdateHotkeySuppression(); _ = guideWindow.ShowDialog(this);
    }
    internal async Task ShowNotesAsync(bool automatic)
    {
        if (notesWindow != null) { notesWindow.Activate(); return; }
        var history = new ReleaseNotesHistory(ReadArg("--history-path"));
        if (automatic && !history.ShouldShow(Program.Version)) return;
        notesWindow = new NotesWindow(); RegisterChild(notesWindow); UpdateHotkeySuppression();
        bool shown = false; notesWindow.Opened += (_, _) => shown = true;
        await notesWindow.ShowDialog(this);
        notesWindow = null; UpdateHotkeySuppression();
        if (shown && !Program.Preview) history.MarkViewed(Program.Version);
    }
    private void UpdateHotkeySuppression() => Engine.HotkeysSuspended = maskGuide.IsVisible || notesWindow != null || guideWindow != null;
    internal void RegisterChild(Window window)
    {
        children.Add(window); window.Closed += (_, _) => children.Remove(window);
    }
    internal async Task<EngineAnswer> ShowPromptAsync(EnginePrompt prompt)
    {
        if (closing) return new(false);
        var window = new PromptWindow(prompt); RegisterChild(window);
        return await window.ShowDialog<EngineAnswer>(this) ?? window.CancelAnswer;
    }
    private async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { if (!closing) await ShowPromptAsync(new EnginePrompt("操作未完成", error.Message, false, null)); }
    }
    private void ActivateFromOutside() => Dispatcher.UIThread.Post(() =>
    {
        if (closing || State.SelectingRange) return;
        Show(); WindowState = WindowState.Normal;
        (children.LastOrDefault(w => w.IsVisible) ?? this).Activate();
    });
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true; if (closing) return; closing = true;
        maskChosen.TrySetResult();
        foreach (var child in children.ToArray()) child.Close();
        try { await Engine.ShutdownAsync(); }
        catch (Exception error) { Logger.Error("[desktop-shutdown] " + error); }
        finally { allowClose = true; Program.ActivateRequested = null; Close(); }
    }
    private async Task CheckUpdatesAsync()
    {
        var result = await ReleaseUpdateChecker.CheckAsync();
        if (closing || updateLink == null) return;
        if (result.Tag == null)
        {
            Logger.Warning("[update] 本次自动检查更新失败，不影响取色和填涂。" + result.Failure);
            updateUrl = ReleaseUpdateChecker.LatestReleaseUrl; ToolTip.SetTip(updateLink, "暂未检查到更新，可点击查看发布页。"); return;
        }
        if (Version.TryParse(result.Tag.TrimStart('v', 'V'), out var version) && version > new Version(Program.Version))
        {
            updateUrl = ReleaseUpdateChecker.RepositoryUrl + "/releases/tag/" + Uri.EscapeDataString(result.Tag);
            updateLink.Content = "新版本 " + result.Tag;
        }
    }
    private void OpenUrl(string url) => Run(() => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); return Task.CompletedTask; });
    internal static string? ReadArg(string key) { var i = Array.IndexOf(Program.Arguments, key); return i >= 0 && i + 1 < Program.Arguments.Length ? Program.Arguments[i + 1] : null; }
}
