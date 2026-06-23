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

        // ======== 填色状态专用速度参数（连续按空格填涂时生效） ========
        // 填色状态下发送按键前的延迟（毫秒）。取色状态仍使用 ActionDelayMs。
        // 注意：过低（<30ms）会导致目标程序来不及处理空格事件，造成漏涂。
        public int FillActionDelayMs { get; set; } = 20;
        // 填色状态下空格连续发送次数。取色状态仍使用 SpaceRepeatCount。
        // 保持 2 次以防被吞，但间隔缩短以提升总体速度。
        public int FillSpaceRepeatCount { get; set; } = 2;
        // 填色状态下连续发送空格之间的间隔（毫秒）。
        public int FillSpaceRepeatGapMs { get; set; } = 10;

        // ======== 同色区域快速划过算法参数 ========
        // 填色聚类中，判定两个网格点属于同一同色区域的最大步长距离（1=直接相邻, 2=间隔1格也能合并）
        public int ClusterNeighborDistance { get; set; } = 2;
        // 快速划过区域内，连续两个填色点之间的额外等待（毫秒），0=不等待
        public int ClusterFillStepDelayMs { get; set; } = 0;

        // 调试探针坐标（可选）
        public int? ProbeX { get; set; }
        // 调试探针坐标（可选）
        public int? ProbeY { get; set; }
    }
}
