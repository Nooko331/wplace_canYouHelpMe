using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Avalonia.Threading;

namespace Wplace.ToolbarPrototype;

public sealed class ToolbarState : INotifyPropertyChanged, IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch clock = new();
    private double progress = 62;
    private string remaining = "00:42";
    private string status = "样板预览";
    private bool busy, showRange = true, extreme;
    private int step = 10;
    private double duration;
    private string mode = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public ToolbarState() { timer.Tick += (_, _) => Tick(); }
    public double Progress { get => progress; private set { progress = value; Changed(); Changed(nameof(Percent)); } }
    public string Percent => $"{(int)Progress}%";
    public string Remaining { get => remaining; private set { remaining = value; Changed(); } }
    public string Status { get => status; set { status = value; Changed(); } }
    public bool Busy { get => busy; private set { busy = value; Changed(); Changed(nameof(CanEdit)); } }
    public bool CanEdit => !Busy;
    public bool ShowRange { get => showRange; set { showRange = value; Changed(); } }
    public bool Extreme { get => extreme; set { extreme = value; Changed(); } }
    public int Step { get => step; set { step = Math.Clamp(value, 1, 64); Changed(); } }
    public string HoverColor { get; private set; } = "#AC7A84";
    public string BaseColor { get; private set; } = "#600018";
    public string RangeDescription { get; private set; } = "示例范围 · 640 × 480 px";
    public bool HasRange { get; private set; } = true;
    public HashSet<string> Wanted { get; } = ["#FFFFFF", "#600018", "#ED1C24", "#FF7F27", "#FFF200", "#22B14C", "#0066FF", "#A349A4"];
    public HashSet<string> Excluded { get; } = ["#7F7F7F", "#9C8470"];
    public static readonly string[] Palette = ["#000000", "#7F7F7F", "#FFFFFF", "#600018", "#ED1C24", "#FF7F27", "#FFF200", "#22B14C", "#99D9EA", "#0066FF", "#3F48CC", "#A349A4", "#FFAEC9", "#B5E61D", "#9C8470", "#EFE4B0"];
    public void Start(bool missing = false)
    {
        if (Busy) return;
        mode = missing ? "遗漏检测演示" : "自动填涂演示";
        duration = missing ? 15 : Extreme ? 15 : 30;
        Progress = 0; Remaining = FormatTime(duration); Busy = true; Status = mode;
        clock.Restart(); timer.Start();
    }
    private void Tick()
    {
        var ratio = Math.Clamp(clock.Elapsed.TotalSeconds / duration, 0, 1);
        Progress = Math.Floor(ratio * 100);
        Remaining = FormatTime(Math.Ceiling(duration - clock.Elapsed.TotalSeconds));
        Status = mode + (ratio < .28 ? " · 扫描" : " · 填涂");
        if (ratio >= 1) { timer.Stop(); clock.Stop(); Busy = false; Remaining = "00:00"; Status = "演示完成"; }
    }
    public void Stop()
    {
        if (!Busy) return;
        timer.Stop(); clock.Stop(); Busy = false; Remaining = "--:--"; Status = "演示已停止 · Esc";
    }
    public void SetPreview(double value, string eta = "00:42")
    {
        Stop(); Progress = Math.Clamp(value, 0, 100); Remaining = eta; Status = "样板预览";
    }
    public void CycleSwatches()
    {
        if (Busy) return;
        var original = BaseColor == "#600018";
        HoverColor = original ? "#8ABF92" : "#AC7A84";
        BaseColor = original ? "#369B49" : "#600018";
        Changed(nameof(HoverColor)); Changed(nameof(BaseColor));
    }
    public void SetRange(double width, double height)
    {
        HasRange = true;
        RangeDescription = $"演示范围 · {(int)width} × {(int)height} px";
        Changed(nameof(RangeDescription)); Changed(nameof(HasRange));
    }
    public void NotifyColorsChanged() => Changed(nameof(Wanted));
    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose() { timer.Stop(); clock.Stop(); }
}
