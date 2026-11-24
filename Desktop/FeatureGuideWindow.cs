using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Wplace.Desktop;

internal sealed record GuideTopic(string Id, string Title, string Icon, string Summary, string[] Steps, string Tip);

internal sealed class FeatureGuideWindow : PixelWindow
{
    internal static readonly GuideTopic[] Topics =
    [
        new("start", "开始使用", "play", "可配合 BlueMarble 或 WPlace 官方蒙版，在画布上完成取色、划定范围和自动填涂。",
            ["打开 WPlace 的选色／填涂界面，确认 BlueMarble 或官方蒙版的参考像素已经显示。", "把工具窗口放到画布与检测范围之外。必要时切到英文输入法。", "划取检测范围，检查采样点，再选择速度并开始自动填涂。"], "先用一小块范围确认步长与页面缩放合适，再扩大范围。"),
        new("sampling", "取色与双颜色", "pen", "A 键会记录鼠标悬停时的颜色，以及移开鼠标后露出的底色。",
            ["把鼠标停在目标像素上，按 A。请勿在本程序窗口上取色。", "程序先读取悬停颜色，再临时移开鼠标等待底色稳定。", "普通取色会调用原有的网页取色动作；颜色管理的连续选色只记录规则。"], "两个色块展示原始采样 RGB，不会把它们替换成色板编号。"),
        new("range", "矩形与多边形", "range", "在鼠标所在屏幕划取检测范围。矩形和正交多边形共用同一个入口。",
            ["点击“划取检测范围”。按住左键拖动，松开即可确认矩形。", "如果只单击而不拖动，则开始绘制正交多边形；继续单击添加水平或垂直线段。", "回到起点闭合。右键撤销顶点，Esc 取消本次选择。"], "这里的图仅为操作示意。实际范围直接在屏幕上选择，不是在程序内部的地图里选择。"),
        new("preview", "显示范围与扫描点", "eye", "眼睛按钮控制屏幕上的范围边框及采样点。",
            ["先完成范围选择，再打开“显示范围与扫描点”。", "观察采样点是否落在目标像素方块内部。若不合适，调整步长或重新划取。", "扫描与取色时覆盖层会暂时隐藏，避免红框红点进入采样；之后恢复。"], "隐藏预览不会清除已记录的检测范围。"),
        new("rules", "颜色管理", "palette", "用想填和排除两组颜色决定本次任务处理哪些颜色。",
            ["进入颜色管理后，可在 63 色色板中直接加入想填或排除。", "也可进入连续选色模式，在网页画布上按 A 采集；Esc 或“停止选色”返回。", "点击已选色块移除。“全部颜色”选中 63 色并清空排除；“清空”只清空想填。"], "想填为空表示默认全部颜色，再减去排除。相同颜色只保留在最后加入的一组；规则仅对本次运行有效。"),
        new("step", "CPU 与扫描步长", "cpu", "CPU 数量控制扫描并行度；步长控制采样点之间的距离。",
            ["CPU 数量仅在完整模式调整。“自动”取约一半逻辑处理器。", "步长可以输入正整数，也可使用上下按钮和键盘上下键。", "步长应与画布像素方块的显示尺寸配合。开启点阵预览后调整更直观。"], "CPU 数量不会改变填涂按键的节奏；步长太大可能漏掉窄小区域。"),
        new("speed", "平衡与极致速度", "gauge", "两种预设调整填涂时的按键间隔与重复次数。",
            ["一般先使用平衡模式。", "页面响应稳定时可尝试极致速度。发生遗漏时停止任务，再切回平衡。", "两种速度都保留非白色取色后的首次验证；白色色组按原来的免检规则处理。"], "示意条表示节奏差异，不是实际耗时对比。网络、页面渲染和取色等待也会影响速度。"),
        new("fill", "自动填涂与进度", "play", "对当前范围扫描、归组，再按颜色执行填涂。",
            ["先选范围，确认颜色规则与步长，再点击开始。", "扫描完成后，按色组处理：白色优先；非白色首次涂色验证成功后继续同色点。", "任务进行时锁定参数。Esc 可全局停止，等待当前任务结束后再修改。"], "精简模式只显示百分比和预计剩余时间；完整模式同时显示已处理与总点数。时间是当前阶段的估算。"),
        new("missing", "遗漏检测与补涂", "missing", "Blue Marble 查找孤岛，官方蒙版查找范围内的半透明采样点。",
            ["在完整布局选择蒙版类型，完成正常填涂后运行遗漏检测。开始前提醒可选择“不再提示”。", "若发现候选点，先查看标记，再确认是否补涂。取消不会补涂。", "补涂沿用颜色规则、速度与首次验证流程。没有候选点时会显示结果说明。"], "“不再提示”只跳过开始前的建议，不会跳过发现候选点后的补涂确认。"),
        new("layout", "布局与快捷键", "compact", "完整模式提供全部设置和说明；精简模式保留一行常用操作。",
            ["每次启动默认进入完整模式，不记忆上次布局。右上角切到精简；展开图标返回完整模式。", "精简模式用鼠标悬停或 Tab 聚焦查看功能浮窗。CPU 数量仍沿用完整模式的设置。", "A 取色，Esc 停止／取消。功能介绍、更新说明和项目链接只放在完整模式。"], "每个版本的更新说明首次启动会自动显示；关掉后可从完整模式重新查看。")
    ];
    private readonly StackPanel article = new() { Spacing = 14 };
    private readonly List<Button> navigation = [];
    internal int SelectedIndex { get; private set; }
    internal FeatureGuideWindow() : base("功能介绍", 920, "book")
    {
        Body.Spacing = 8;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("184,20,*") };
        var menu = new StackPanel { Spacing = 6 };
        menu.Children.Add(Ui.Note("目录", 13));
        for (int i = 0; i < Topics.Length; i++)
        {
            var index = i; var topic = Topics[i];
            var button = Ui.Button(topic.Title, () => SelectTopic(index), topic.Icon, id: "GuideTopic" + i);
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new Thickness(8, 4); button.FontSize = 12;
            menu.Children.Add(button); navigation.Add(button);
        }
        grid.Children.Add(menu);
        var pane = new ScrollViewer { Height = 510, Content = article, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetColumn(pane, 2); grid.Children.Add(pane);
        Body.Children.Add(grid); SelectTopic(0);
    }
    internal void SelectTopic(int index)
    {
        SelectedIndex = Math.Clamp(index, 0, Topics.Length - 1);
        var topic = Topics[SelectedIndex];
        for (int i = 0; i < navigation.Count; i++) navigation[i].Classes.Set("selected", i == SelectedIndex);
        article.Children.Clear(); article.Children.Add(Ui.Text(topic.Title, 23, true)); article.Children.Add(Ui.Text(topic.Summary, 14));
        article.Children.Add(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Background = Ui.Pale, Child = new GuideFigure(topic.Id) { Height = 180 } });
        article.Children.Add(Ui.Note("操作示意 · 非实际画布", 11));
        for (int i = 0; i < topic.Steps.Length; i++)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*") };
            row.Children.Add(Ui.Text((i + 1).ToString("00"), 15, true)); var text = Ui.Text(topic.Steps[i], 14); Grid.SetColumn(text, 1); row.Children.Add(text); article.Children.Add(row);
        }
        article.Children.Add(new Border { Background = Ui.Pale, Padding = new Thickness(14), Child = Ui.Note(topic.Tip, 13) });
    }
}

internal sealed class GuideFigure : Control
{
    private readonly string kind;
    internal GuideFigure(string kind) { this.kind = kind; ClipToBounds = true; RenderOptions.SetEdgeMode(this, EdgeMode.Aliased); }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scale = Math.Min(Bounds.Width / 580, Bounds.Height / 180);
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale));
        IBrush blue = Ui.Blue, navy = Ui.Navy, white = Brushes.White, muted = Brush.Parse("#B5C9E4"), red = Brush.Parse("#EF3044");
        void Box(double x, double y, double w, double h, IBrush fill, IBrush? line = null) => context.DrawRectangle(fill, line == null ? null : new Pen(line, 2), new Rect(x, y, w, h));
        void L(double x, double y, double xx, double yy, IBrush color, double thick = 3) => context.DrawLine(new Pen(color, thick), new Point(x, y), new Point(xx, yy));
        void Arrow(double x, double y) { L(x, y, x + 46, y, blue, 4); L(x + 34, y - 10, x + 46, y, blue, 4); L(x + 34, y + 10, x + 46, y, blue, 4); }
        void Grid(double x, double y, int cols, int rows, int spacing = 20)
        {
            for (int yy = 0; yy < rows; yy++) for (int xx = 0; xx < cols; xx++)
                Box(x + xx * spacing, y + yy * spacing, spacing - 1, spacing - 1, Brush.Parse(xx + yy % 3 < cols / 2 ? "#92B7A4" : "#8CAED4"));
        }
        if (kind is "range" or "preview" or "step" or "missing")
        {
            Grid(24, 20, 12, 7); Grid(316, 20, 12, 7); Arrow(275, 90);
            if (kind == "range")
            {
                context.DrawRectangle(null, new Pen(white, 5), new Rect(44, 40, 180, 100)); context.DrawRectangle(null, new Pen(red, 2), new Rect(44, 40, 180, 100));
                var points = new[] { new Point(336, 40), new Point(516, 40), new Point(516, 100), new Point(416, 100), new Point(416, 140), new Point(336, 140), new Point(336, 40) };
                for (int i = 1; i < points.Length; i++) { L(points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y, white, 6); L(points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y, red, 2); }
                foreach (var point in points) Box(point.X - 4, point.Y - 4, 8, 8, blue, white);
            }
            else
            {
                int spacing = kind == "step" ? 40 : 20;
                for (int y = 30; y < 155; y += 20) for (int x = 34; x < 260; x += 20) Box(x, y, 3, 3, red);
                for (int y = 30; y < 155; y += spacing) for (int x = 326; x < 552; x += spacing)
                    if (kind != "missing" || (x is > 400 and < 460 && y is > 60 and < 110)) Box(x, y, kind == "missing" ? 7 : 3, kind == "missing" ? 7 : 3, red);
                if (kind == "missing") context.DrawRectangle(null, new Pen(white, 3), new Rect(394, 62, 74, 58));
            }
        }
        else if (kind is "sampling" or "rules")
        {
            string[] colors = ["#AC7A84", "#600018", "#ED1C24", "#FF7F27", "#FFF200", "#22B14C", "#0066FF", "#A349A4"];
            if (kind == "sampling")
            {
                Grid(30, 25, 7, 6); Box(91, 66, 38, 38, Brush.Parse(colors[0]), white); Arrow(196, 90);
                Box(270, 45, 88, 88, Brush.Parse(colors[0]), navy); Arrow(385, 90); Box(456, 45, 88, 88, Brush.Parse(colors[1]), navy);
                L(115, 80, 115, 111, white, 3); L(105, 94, 126, 94, white, 3);
            }
            else
            {
                for (int i = 0; i < colors.Length; i++) Box(28 + i * 66, 28, 48, 48, Brush.Parse(colors[i]), navy);
                L(28, 103, 552, 103, muted, 1);
                for (int i = 0; i < 5; i++) { Box(28 + i * 66, 124, 48, 30, Brush.Parse(colors[i + 1]), blue); }
                Box(438, 124, 48, 30, Brush.Parse(colors[6]), red); Box(504, 124, 48, 30, Brush.Parse(colors[7]), red);
            }
        }
        else if (kind == "speed")
        {
            for (int i = 0; i < 9; i++) Box(50 + i * 54, 43, 24, 28, blue);
            for (int i = 0; i < 17; i++) Box(50 + i * 28, 109, 16, 28, blue);
            L(35, 90, 545, 90, muted, 1);
        }
        else if (kind == "layout")
        {
            Box(30, 22, 190, 136, white, navy); Box(40, 32, 170, 20, blue); Box(40, 66, 100, 58, Ui.Pale); Box(148, 66, 62, 58, muted); Box(40, 134, 170, 12, blue);
            Arrow(244, 90); Box(310, 65, 240, 50, white, navy);
            for (int i = 0; i < 7; i++) Box(320 + i * 31, 77, 24, 24, i == 4 ? blue : Ui.Pale, muted);
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                int x = 34 + i * 190; Box(x, 35, 130, 105, white, navy); Box(x + 12, 47, 106, 14, muted);
                if (i == 0) Grid(x + 15, 72, 5, 3, 19);
                if (i == 1) { Box(x + 20, 79, 35, 35, Brush.Parse("#600018"), navy); Box(x + 70, 79, 35, 35, Brush.Parse("#AC7A84"), navy); }
                if (i == 2) { Box(x + 15, 83, 100, 22, Ui.Pale, muted); Box(x + 17, 85, 78, 18, blue); }
                if (i < 2) Arrow(x + 139, 91);
            }
        }
    }
}
