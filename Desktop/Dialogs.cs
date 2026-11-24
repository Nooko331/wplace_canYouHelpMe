using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using WplaceColorWatch;

namespace Wplace.Desktop;

internal sealed class NotesWindow : PixelWindow
{
    internal NotesWindow() : base("更新说明 · v" + Program.Version, 680, "notes")
    {
        using var stream = typeof(NotesWindow).Assembly.GetManifestResourceStream("Wplace.Desktop.ReleaseNotes.txt") ?? throw new InvalidOperationException("缺少更新说明资源。");
        using var reader = new StreamReader(stream);
        var notes = reader.ReadToEnd().ReplaceLineEndings("\n");
        Body.Spacing = 8;
        foreach (var section in ("\n" + notes).Split("\n## ", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = section.Split('\n', 2);
            var title = lines[0].Trim();
            var content = new StackPanel { Spacing = 10 };
            foreach (var paragraph in lines[1].Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                bool heading = !paragraph.Contains('\n') && paragraph.Length < 25 && !paragraph.EndsWith('。');
                content.Children.Add(heading ? Ui.Text(paragraph, 15, true) : Ui.Text(paragraph, 14));
            }
            var item = new Expander
            {
                Header = Ui.Text(title, 16, true), Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = title.Split(' ', 2)[0] == "v" + Program.Version
            };
            AutomationProperties.SetName(item, title);
            Body.Children.Add(item);
        }
        var ok = Ui.Button("知道了", Close, primary: true, id: "AcknowledgeNotes"); ok.HorizontalAlignment = HorizontalAlignment.Right;
        Footer.Child = ok; Footer.IsVisible = true;
    }
}

internal sealed class PromptWindow : PixelWindow
{
    private readonly CheckBox? check;
    internal EngineAnswer CancelAnswer => new(false, check?.IsChecked == true);
    internal PromptWindow(EnginePrompt prompt) : base(prompt.Title, 530, prompt.Confirm ? "missing" : "info")
    {
        Body.Children.Add(Ui.Text(prompt.Message, 15));
        if (prompt.CheckBox != null) { check = new CheckBox { Content = prompt.CheckBox }; Body.Children.Add(check); }
        var actions = Ui.Row(); actions.HorizontalAlignment = HorizontalAlignment.Right;
        if (prompt.Confirm) actions.Children.Add(Ui.Button("取消", () => Close(CancelAnswer), id: "CancelPrompt"));
        actions.Children.Add(Ui.Button(prompt.Confirm ? "确认继续" : "知道了", () => Close(new EngineAnswer(true, check?.IsChecked == true)), primary: true, id: "ConfirmPrompt"));
        Body.Children.Add(actions);
    }
    protected override void OnEscape() => Close(CancelAnswer);
}

internal sealed class ColorManagerWindow : PixelWindow
{
    private readonly MainWindow main;
    private readonly StackPanel home = new() { Spacing = 14 }, picking = new() { Spacing = 16 };
    private readonly WrapPanel wanted = new(), excluded = new(), pickColors = new(), palette = new();
    private readonly TextBlock wantedHeading = Ui.Text("我想填的颜色", 19, true), excludedHeading = Ui.Text("我不想填的颜色", 19, true), pickHeading = Ui.Text("", 20, true), pickMessage = Ui.Note(), ruleSummary = Ui.Note();
    private readonly Button wantedTab, excludedTab;
    private readonly List<(Button Button, string Hex)> paletteButtons = [];
    private DesktopSnapshot state = new();
    private bool excludeTarget;
    private string signature = "";
    internal ColorManagerWindow(MainWindow main) : base("颜色管理", 790, "palette")
    {
        this.main = main;
        home.Children.Add(Ui.Note("内置 63 色 · 颜色规则仅对本次运行有效"));
        home.Children.Add(RuleHeader(wantedHeading, Ui.Button("进入选色模式", () => Command(DesktopCommand.PickWanted), primary: true, id: "PickWanted"), Ui.Button("全部颜色", () => Command(DesktopCommand.SelectAllColors), id: "AllColors"), Ui.Button("清空", () => Command(DesktopCommand.ClearWanted), id: "ClearWanted")));
        home.Children.Add(SwatchArea(wanted)); home.Children.Add(Ui.Note("点击已选色块可以移除。想填列表为空时，默认使用全部颜色。")); home.Children.Add(Ui.Rule());
        home.Children.Add(RuleHeader(excludedHeading, Ui.Button("进入选色模式", () => Command(DesktopCommand.PickExcluded), id: "PickExcluded")));
        home.Children.Add(SwatchArea(excluded)); home.Children.Add(Ui.Note("排除颜色会从想填列表移除；同色只保留在最后选择的一类。")); home.Children.Add(Ui.Rule());
        wantedTab = Ui.Button("加入想填", () => SetTarget(false), id: "PaletteWanted"); excludedTab = Ui.Button("加入排除", () => SetTarget(true), id: "PaletteExcluded");
        home.Children.Add(Ui.Row(Ui.Text("直接从色板选择", 16, true), wantedTab, excludedTab));
        foreach (var hex in WindowsEngineHost.Palette)
        {
            var color = hex;
            var button = new Button { Width = 34, Height = 34, Margin = new Thickness(2), Padding = new Thickness(4), Content = new Border { Width = 24, Height = 24, Background = Brush.Parse(hex) }, Classes = { "pixel" } };
            AutomationProperties.SetName(button, "色板 " + hex);
            button.Click += (_, _) => Command(excludeTarget ? DesktopCommand.AddExcluded : DesktopCommand.AddWanted, color);
            paletteButtons.Add((button, hex)); palette.Children.Add(Ui.Hint(button, hex, () => Ui.Rgb(color) + (excludeTarget ? "\n单击加入排除。" : "\n单击加入想填。")));
        }
        home.Children.Add(palette);
        home.Children.Add(new Border { Background = Ui.Pale, Padding = new Thickness(13), Child = ruleSummary });

        picking.Children.Add(pickHeading);
        picking.Children.Add(new Border { Background = Ui.Pale, Padding = new Thickness(20), Child = Ui.Column(Ui.Row(Ui.Key("A"), Ui.Text("鼠标移到画布颜色上，按 A 加入", 19, true)), Ui.Note("连续选择，不发送取色或填涂按键。Esc 返回颜色管理。")) });
        picking.Children.Add(new GuideFigure("sampling") { Height = 180 });
        picking.Children.Add(pickMessage); picking.Children.Add(SwatchArea(pickColors));
        picking.Children.Add(Ui.Button("停止选色 · Esc", () => Command(DesktopCommand.StopPicking), primary: true, id: "StopColorPicking"));
        picking.Children.Add(Ui.Note("取色不稳定时不会加入列表，请停稳鼠标后重试。"));
        Body.Children.Add(home); Body.Children.Add(picking); picking.IsVisible = false; SetTarget(false);
    }
    private static Grid RuleHeader(TextBlock title, params Button[] buttons)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; grid.Children.Add(title);
        foreach (var button in buttons) { button.MinWidth = 60; button.Padding = new Thickness(11, 5); }
        var row = Ui.Row(buttons); Grid.SetColumn(row, 1); grid.Children.Add(row); return grid;
    }
    private static Border SwatchArea(WrapPanel panel) => new() { MinHeight = 50, Background = Brush.Parse("#F8FBFF"), Padding = new Thickness(6), Child = panel };
    private async void Command(DesktopCommand command, string? color = null)
    {
        try { await main.Engine.ExecuteAsync(command, color); UpdateState(main.Engine.Snapshot); }
        catch (Exception error) { await main.ShowPromptAsync(new EnginePrompt("颜色规则未更新", error.Message, false, null)); }
    }
    private void SetTarget(bool exclude) { excludeTarget = exclude; wantedTab?.Classes.Set("selected", !exclude); excludedTab?.Classes.Set("selected", exclude); UpdatePalette(); }
    internal void UpdateState(DesktopSnapshot snapshot)
    {
        state = snapshot;
        bool isPicking = snapshot.Picking.Length > 0;
        home.IsVisible = !isPicking; picking.IsVisible = isPicking;
        string next = string.Join(',', snapshot.Wanted) + "|" + string.Join(',', snapshot.Excluded) + "|" + snapshot.Picking;
        if (signature != next)
        {
            signature = next;
            FillSwatches(wanted, snapshot.Wanted, DesktopCommand.RemoveWanted); FillSwatches(excluded, snapshot.Excluded, DesktopCommand.RemoveExcluded);
            bool wantedMode = snapshot.Picking == "Wanted";
            FillSwatches(pickColors, wantedMode ? snapshot.Wanted : snapshot.Excluded, wantedMode ? DesktopCommand.RemoveWanted : DesktopCommand.RemoveExcluded);
            UpdatePalette();
        }
        wantedHeading.Text = $"我想填的颜色 · {snapshot.Wanted.Count}";
        excludedHeading.Text = $"我不想填的颜色 · {snapshot.Excluded.Count}";
        pickHeading.Text = snapshot.Picking == "Wanted" ? "正在选择：我想填的颜色" : "正在选择：我不想填的颜色";
        pickMessage.Text = snapshot.PickMessage;
        ruleSummary.Text = $"当前可填 {snapshot.EffectiveCount} / 63 色。\n全部颜色：选中 63 色并清空排除。清空：只清空想填列表。";
    }
    private void FillSwatches(WrapPanel panel, IReadOnlyList<string> colors, DesktopCommand remove)
    {
        panel.Children.Clear();
        if (colors.Count == 0) { panel.Children.Add(Ui.Note(remove == DesktopCommand.RemoveWanted ? "未指定，默认全部颜色" : "尚未排除颜色")); return; }
        foreach (var hex in colors)
        {
            var color = Color.Parse(hex);
            var symbol = Ui.Text("×", 20); symbol.HorizontalAlignment = HorizontalAlignment.Center;
            symbol.Foreground = (color.R * 299 + color.G * 587 + color.B * 114) / 1000 >= 145 ? Brushes.Black : Brushes.White;
            var b = new Button { Width = 44, Height = 44, Margin = new Thickness(3), Content = new Border { Background = Brush.Parse(hex), Width = 30, Height = 30, Child = symbol }, Classes = { "pixel" }, Padding = new Thickness(3) };
            AutomationProperties.SetName(b, "移除 " + hex); b.Click += (_, _) => Command(remove, hex);
            panel.Children.Add(Ui.Hint(b, hex, () => Ui.Rgb(hex) + "\n单击移除。"));
        }
    }
    private void UpdatePalette()
    {
        foreach (var item in paletteButtons) item.Button.Classes.Set("selected", (excludeTarget ? state.Excluded : state.Wanted).Contains(item.Hex));
    }
    protected override void OnEscape()
    {
        if (state.Picking.Length > 0) Command(DesktopCommand.StopPicking);
        else Close();
    }
}
