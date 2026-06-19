namespace WplaceColorWatch
{

    public sealed class Options
    {
        // 是否输出调试日志
        public bool Debug { get; set; }
        // UI/检测刷新间隔（毫秒）
        public int IntervalMs { get; set; } = 100;
        // 颜色匹配容差（0 表示完全一致）
        public int ColorTol { get; set; } = 0;
        // 触发动作的冷却时间（毫秒）
        public int CooldownMs { get; set; } = 15;
        // 自动填充扫描步长（像素）
        public int ScanStep { get; set; } = 10;
        // 自动填充扫描线程数
        public int ScanWorkers { get; set; } = 1;
        // 对目标窗口执行动作前的延迟（毫秒）
        public int ActionDelayMs { get; set; } = 50;
        // 空格连续发送次数（>=1）：大量填涂时可能出现卡顿导致单次空格被吞掉，重复发送可保证至少触发一次
        public int SpaceRepeatCount { get; set; } = 2;
        // 连续发送空格之间的间隔（毫秒）
        public int SpaceRepeatGapMs { get; set; } = 30;
        // 取色（i+鼠标左键）完成后到触发空格之间的间隔（毫秒），过短可能导致空格触发无效
        public int ColorPickToFillDelayMs { get; set; } = 150;
        // 调试探针坐标（可选）
        public int? ProbeX { get; set; }
        // 调试探针坐标（可选）
        public int? ProbeY { get; set; }
    }
}
