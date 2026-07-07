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

        // ======== 遗漏点孤岛检测参数 ========
        // 是否在自动/全自动填涂完成后自动运行遗漏点检测
        public bool IslandDetectEnabled { get; set; }
        // 小簇绝对上限：簇点数 ≤ 此值才作为孤岛候选（兜底，防极大簇）。应介于“遗漏点数”与“已处理点数”之间
        public int IslandMaxSize { get; set; } = 5;
        // 护城河阈值：小簇紧邻外环中“非匹配点”占比需 ≥ 此阈值（0~1）
        public double IslandMoatRatio { get; set; } = 0.5;
        // 是否要求护城河外围存在同色大簇——区分“漏涂残留”与“图案本身独立小色块”的关键判据
        public bool IslandRequireOuterBig { get; set; } = true;
        // 外围同色大簇搜索半径（网格层数）：从护城河向外 BFS 的最大层数，需大于实际护城河宽度
        public int IslandSearchRadius { get; set; } = 6;
        // 外围大簇相对比例：大簇点数需 ≥ 孤岛点数 × 此值。步长无关的稳定判据，对应“已处理至少是遗漏的 N 倍”规律
        public double IslandMinOuterMultiplier { get; set; } = 3.0;
        // 强护城河阈值：当外环非匹配占比 ≥ 此值时，跳过“外围同色大簇”条件。
        // 完美护城河本身已是强信号（图案独立小色块外环不会全是未匹配）。0.9=占比≥90%即跳过条件3
        public double IslandStrongMoatRatio { get; set; } = 0.9;
        // 遗漏检测专用颜色容差：独立于全局 ColorTol。遗漏点(已涂但未匹配)往往因悬停蒙板/抗锯齿与预设色差 1~3 度，
        // 全局 ColorTol=0 会令它们永远进不了网格 → 算法看不见 → 再检测也测不出。此处应设得比 ColorTol 宽（建议 8~12）
        public int IslandColorTol { get; set; } = 10;
        // 遗漏检测：是否在“未找到遗漏点”时输出诊断日志（各簇大小分布、非匹配洞数量），用于定位“测不出”的根因
        public bool IslandDiagnose { get; set; } = true;

        // 调试探针坐标（可选）
        public int? ProbeX { get; set; }
        // 调试探针坐标（可选）
        public int? ProbeY { get; set; }
    }
}
