namespace WplaceColorWatch
{

public sealed class Options
{
    public bool Debug { get; set; }
    public int IntervalMs { get; set; } = 50;
    public int ColorTol { get; set; } = 0;
    public int CooldownMs { get; set; } = 80;
    public int ScanStep { get; set; } = 10;
    public int ScanWorkers { get; set; } = 1;
    public int ActionDelayMs { get; set; } = 50;
    public int? ProbeX { get; set; }
    public int? ProbeY { get; set; }
}
}

