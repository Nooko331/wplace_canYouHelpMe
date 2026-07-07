using System;
using System.Collections.Generic;
using System.Drawing;

namespace WplaceColorWatch
{
    /// <summary>
    /// 遗漏点孤岛检测：在带标签网格上找出“同色小簇 + 非匹配护城河 + 外围同色大簇”的同心圆结构，
    /// 识别 BlueMarble 已渲染但未被成功填涂的方块（遗漏点）。
    ///
    /// 关键：采样采用品字形（奇数行偏移 step/2，即六边形采样），邻居关系是六边形的6邻居，
    /// 而非矩形正交4邻居。本检测器用 (col,row) 六边形坐标 + odd-r 邻居规则正确表达邻接，
    /// 否则连续大陆会被沿偶/奇行错误切断，导致簇大小与比例判据全部失真。
    /// 纯算法、无 UI 依赖，便于单测。
    /// </summary>
    public static class IslandDetector
    {
        /// <summary>一个被判定为遗漏孤岛的同色小簇。</summary>
        public sealed class Island
        {
            public BgrColor Color { get; set; }
            public List<Point> Points { get; set; } = new();
        }

        /// <summary>
        /// 在完整采样网格上检测遗漏孤岛。
        /// </summary>
        /// <param name="grid">完整采样网格：hex key → 标签。value 为 null 表示该采样点“未匹配任何预设色”（即护城河来源）；非 null 表示匹配到该色。必须包含所有采样点（含未匹配），否则护城河判定会失真。</param>
        /// <param name="rect">框选范围（屏幕坐标），用于 hex→像素 反推</param>
        /// <param name="step">网格步长（像素）</param>
        /// <param name="maxIslandSize">小簇绝对上限：簇点数 ≤ 此值才作为孤岛候选（兜底，防极大簇）</param>
        /// <param name="moatRatio">护城河阈值：紧邻外环中非匹配点占比需 ≥ 此值</param>
        /// <param name="requireOuterSameColorBig">是否要求护城河外围存在同色大簇（区分漏涂残留 vs 图案独立小色块）</param>
        /// <param name="outerSearchRadius">外围同色大簇搜索半径（网格层数）：从护城河向外 BFS 的最大层数，需大于实际护城河宽度</param>
        /// <param name="minOuterMultiplier">外围大簇相对比例：大簇点数需 ≥ 孤岛点数 × 此值（步长无关的稳定判据）</param>
        /// <param name="strongMoatRatio">强护城河阈值：当外环非匹配占比 ≥ 此值时，跳过“外围同色大簇”条件（完美护城河本身已是强信号）</param>
        /// <returns>被判为遗漏孤岛的簇列表，每个簇携带其颜色与像素坐标点集</returns>
        public static List<Island> Detect(
            Dictionary<long, BgrColor?> grid,
            Rectangle rect,
            int step,
            int maxIslandSize,
            double moatRatio,
            bool requireOuterSameColorBig,
            int outerSearchRadius,
            double minOuterMultiplier,
            double strongMoatRatio)
        {
            var result = new List<Island>();
            if (grid == null || grid.Count == 0)
            {
                return result;
            }

            int safeStep = Math.Max(1, step);
            int safeRadius = Math.Max(1, outerSearchRadius);

            // 1) 提取匹配点，按颜色分组
            var byColor = new Dictionary<BgrColor, List<long>>();
            foreach (var kv in grid)
            {
                if (kv.Value.HasValue)
                {
                    var color = kv.Value.Value;
                    if (!byColor.TryGetValue(color, out var list))
                    {
                        list = new List<long>();
                        byColor[color] = list;
                    }
                    list.Add(kv.Key);
                }
            }

            // 2) 聚类（六边形邻居，距离1），并建立 pointKey -> 所属簇大小
            var pointToClusterSize = new Dictionary<long, int>();
            var allClusters = new List<(BgrColor Color, List<long> Keys, int Size)>();

            foreach (var kv in byColor)
            {
                var clusters = ClusterHex(kv.Value, grid);
                foreach (var cl in clusters)
                {
                    if (cl.Count == 0)
                    {
                        continue;
                    }
                    allClusters.Add((kv.Key, cl, cl.Count));
                    foreach (var k in cl)
                    {
                        pointToClusterSize[k] = cl.Count;
                    }
                }
            }

            // 3) 对每个小簇做孤岛判定
            foreach (var (color, keys, size) in allClusters)
            {
                if (size > maxIslandSize)
                {
                    continue; // 条件1：绝对小簇上限（兜底）
                }

                var keySet = new HashSet<long>(keys);

                // 外环 = 簇内点的6邻居中、在采样网格内、不在簇内的点
                var outerRing = new HashSet<long>();
                foreach (var k in keys)
                {
                    var (c, r) = FromHexKey(k);
                    foreach (var (nc, nr) in HexNeighbors(c, r))
                    {
                        var nk = ToHexKey(nc, nr);
                        if (!keySet.Contains(nk) && grid.ContainsKey(nk))
                        {
                            outerRing.Add(nk);
                        }
                    }
                }
                if (outerRing.Count == 0)
                {
                    continue; // 紧贴边界 / 无外环，跳过
                }

                // 条件2：护城河——外环中非匹配点(grid[k]==null)占比 ≥ moatRatio
                int nullCount = 0;
                foreach (var k in outerRing)
                {
                    if (!grid[k].HasValue)
                    {
                        nullCount++;
                    }
                }
                double ratio = (double)nullCount / outerRing.Count;
                if (ratio < moatRatio)
                {
                    continue;
                }

                // 条件3：外围同色大簇——从护城河向外逐圈 BFS（穿过任意宽度的非匹配护城河），
                // 在 outerSearchRadius 层内存在同色点且其所属簇 size ≥ 孤岛 size × minOuterMultiplier。
                // 例外：当护城河占比极高(≥ StrongMoatRatio)时跳过此条件——完美的护城河本身已是强信号
                // （图案独立小色块的外环不会全是未匹配点，它周围是别的已涂颜色）。
                if (requireOuterSameColorBig && ratio < strongMoatRatio)
                {
                    long minOuterSize = (long)Math.Ceiling(size * minOuterMultiplier);
                    if (!HasOuterSameColorBig(keySet, outerRing, color, minOuterSize,
                        safeRadius, grid, pointToClusterSize))
                    {
                        continue;
                    }
                }

                // 输出像素坐标
                var pts = new List<Point>(keys.Count);
                foreach (var k in keys)
                {
                    var (c, r) = FromHexKey(k);
                    pts.Add(HexToPixel(c, r, rect, safeStep));
                }
                result.Add(new Island { Color = color, Points = pts });
            }

            return result;
        }

        /// <summary>
        /// 诊断：统计网格内“同色小簇”及其护城河情况，定位“测不出遗漏点”的根因。
        /// 返回多行诊断字符串（数字+调参建议），可直接展示给用户。
        /// </summary>
        public static string Diagnose(Dictionary<long, BgrColor?> grid, int smallThreshold)
        {
            if (grid == null || grid.Count == 0)
            {
                return "[diag] 网格为空，未扫描到任何点";
            }

            var sb = new System.Text.StringBuilder();
            int total = grid.Count;
            int nullCount = 0;
            var byColor = new Dictionary<BgrColor, List<long>>();
            foreach (var kv in grid)
            {
                if (kv.Value.HasValue)
                {
                    var c = kv.Value.Value;
                    if (!byColor.TryGetValue(c, out var l)) { l = new List<long>(); byColor[c] = l; }
                    l.Add(kv.Key);
                }
                else
                {
                    nullCount++;
                }
            }
            int matched = total - nullCount;
            sb.AppendLine($"扫描点={total} 匹配={matched} 未匹配(null)={nullCount}");

            if (matched == 0)
            {
                sb.AppendLine("→ 根因：没有点匹配上颜色。提高 IslandColorTol（颜色容差），或确认框选范围里有 BlueMarble 方块。");
                return sb.ToString();
            }

            // 聚类，收集每个簇的 size
            var allClusters = new List<(BgrColor Color, List<long> Keys, int Size)>();
            foreach (var kv in byColor)
            {
                var clusters = ClusterHex(kv.Value, grid);
                foreach (var cl in clusters)
                {
                    if (cl.Count > 0) allClusters.Add((kv.Key, cl, cl.Count));
                }
            }
            allClusters.Sort((a, b) => a.Size.CompareTo(b.Size));

            int smallCount = 0;
            foreach (var c in allClusters) if (c.Size <= smallThreshold) smallCount++;
            sb.AppendLine($"同色簇总数={allClusters.Count} 其中小簇(≤{smallThreshold})={smallCount}");

            if (smallCount == 0)
            {
                sb.AppendLine("→ 根因：所有匹配点都聚成了大簇，没有小簇可作为遗漏候选。");
                sb.AppendLine("  可能：遗漏点与同色已处理区直接相邻，被聚类合并了。尝试调小扫描步长，让遗漏点与已处理区分开采样。");
                return sb.ToString();
            }

            // 对每个小簇算护城河情况
            int moatOk = 0, moatFail = 0;
            double maxRatio = 0;
            var ratioList = new List<double>();
            foreach (var (color, keys, size) in allClusters)
            {
                if (size > smallThreshold) continue;
                var keySet = new HashSet<long>(keys);
                var outerRing = new HashSet<long>();
                foreach (var k in keys)
                {
                    var (c, r) = FromHexKey(k);
                    foreach (var (nc, nr) in HexNeighbors(c, r))
                    {
                        var nk = ToHexKey(nc, nr);
                        if (!keySet.Contains(nk) && grid.ContainsKey(nk)) outerRing.Add(nk);
                    }
                }
                if (outerRing.Count == 0)
                {
                    sb.AppendLine($"  小簇 size={size}：无外环（紧贴边界）");
                    continue;
                }
                int nc2 = 0;
                foreach (var k in outerRing) if (!grid[k].HasValue) nc2++;
                double ratio = (double)nc2 / outerRing.Count;
                ratioList.Add(ratio);
                if (ratio > maxRatio) maxRatio = ratio;
                if (ratio >= 0.5) moatOk++; else moatFail++;
            }
            if (ratioList.Count > 0)
            {
                var rs = new List<double>(ratioList);
                rs.Sort();
                var parts = new List<string>();
                foreach (var r in rs) parts.Add(r.ToString("0.00"));
                sb.AppendLine($"小簇护城河(null占比)：{string.Join(",", parts.ToArray())}");
                sb.AppendLine($"达0.5阈值的={moatOk} 未达={moatFail} 最大={maxRatio:0.00}");
            }
            if (moatOk == 0)
            {
                sb.AppendLine("→ 根因：小簇的外环大多不是“未匹配点”，护城河判定失败。");
                sb.AppendLine("  可能：遗漏点周围紧挨的是别的已处理颜色（非null）。降低 IslandMoatRatio（如0.2），或这些点本就不是遗漏。");
            }
            else if (moatOk > 0)
            {
                sb.AppendLine("→ 有小簇通过了护城河判定，但仍被判为非遗漏：");
                sb.AppendLine("  多因“外围同色大簇”条件不满足。设 IslandRequireOuterBig=false 可排查，或降低 IslandMinOuterMultiplier（如2.0）。");
            }
            return sb.ToString();
        }

        /// <summary>同色点按六边形邻居 BFS 聚类。</summary>
        private static List<List<long>> ClusterHex(List<long> keys, Dictionary<long, BgrColor?> grid)
        {
            var result = new List<List<long>>();
            if (keys.Count == 0)
            {
                return result;
            }
            var keySet = new HashSet<long>(keys);
            var visited = new HashSet<long>(keys.Count);
            foreach (var start in keys)
            {
                if (visited.Contains(start))
                {
                    continue;
                }
                var cluster = new List<long>();
                var queue = new Queue<long>();
                queue.Enqueue(start);
                visited.Add(start);
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    cluster.Add(cur);
                    var (c, r) = FromHexKey(cur);
                    foreach (var (nc, nr) in HexNeighbors(c, r))
                    {
                        var nk = ToHexKey(nc, nr);
                        if (keySet.Contains(nk) && !visited.Contains(nk))
                        {
                            visited.Add(nk);
                            queue.Enqueue(nk);
                        }
                    }
                }
                result.Add(cluster);
            }
            return result;
        }

        /// <summary>
        /// 从护城河(outerRing)向外 BFS，穿过非匹配/异色区域，在 searchRadius 层内寻找
        /// “同色且所属簇大小 ≥ minOuterSize”的点。找到即说明孤岛外围有同色大簇（漏涂残留特征）。
        /// </summary>
        private static bool HasOuterSameColorBig(
            HashSet<long> keySet,
            HashSet<long> outerRing,
            BgrColor color,
            long minOuterSize,
            int searchRadius,
            Dictionary<long, BgrColor?> grid,
            Dictionary<long, int> pointToClusterSize)
        {
            var visited = new HashSet<long>(outerRing.Count * 2);
            var frontier = new Queue<(long Key, int Depth)>();

            foreach (var k in outerRing)
            {
                visited.Add(k);
                if (IsSameColorBig(k, color, minOuterSize, grid, pointToClusterSize))
                {
                    return true;
                }
                frontier.Enqueue((k, 0));
            }

            while (frontier.Count > 0)
            {
                var (key, depth) = frontier.Dequeue();
                if (depth >= searchRadius)
                {
                    continue;
                }
                var (c, r) = FromHexKey(key);
                foreach (var (nc, nr) in HexNeighbors(c, r))
                {
                    var nk = ToHexKey(nc, nr);
                    if (visited.Contains(nk) || keySet.Contains(nk) || !grid.ContainsKey(nk))
                    {
                        continue; // 不重访、不回到孤岛内部、不越出采样网格
                    }
                    visited.Add(nk);
                    if (IsSameColorBig(nk, color, minOuterSize, grid, pointToClusterSize))
                    {
                        return true;
                    }
                    frontier.Enqueue((nk, depth + 1));
                }
            }
            return false;
        }

        private static bool IsSameColorBig(
            long key, BgrColor color, long minOuterSize,
            Dictionary<long, BgrColor?> grid,
            Dictionary<long, int> pointToClusterSize)
        {
            var v = grid[key];
            if (!v.HasValue)
            {
                return false;
            }
            var c = v.Value;
            if (c.R != color.R || c.G != color.G || c.B != color.B)
            {
                return false;
            }
            return pointToClusterSize.TryGetValue(key, out var cs) && cs >= minOuterSize;
        }

        // ==================== 六边形坐标（odd-r：奇数行向右偏移 step/2） ====================
        // 与 ScanPattern 的采样一致：奇数行 startOffset = step/2。

        internal static long ToHexKey(int col, int row)
        {
            return ((long)col << 32) | (long)(uint)row;
        }

        internal static (int Col, int Row) FromHexKey(long key)
        {
            int col = (int)(key >> 32);
            int row = (int)(key & 0xFFFFFFFF);
            return (col, row);
        }

        /// <summary>hex 坐标 → 屏幕像素坐标（与 ScanPattern 生成规则互逆）。</summary>
        internal static Point HexToPixel(int col, int row, Rectangle rect, int step)
        {
            int offset = (row % 2 == 1) ? (step / 2) : 0;
            return new Point(rect.Left + col * step + offset, rect.Top + row * step);
        }

        /// <summary>屏幕像素 → hex 坐标（仅对采样点精确）。</summary>
        internal static (int Col, int Row) PixelToHex(Point p, Rectangle rect, int step)
        {
            int row = (p.Y - rect.Top) / step;
            int offset = (row % 2 == 1) ? (step / 2) : 0;
            int col = (p.X - rect.Left - offset) / step;
            return (col, row);
        }

        /// <summary>
        /// odd-r 六边形6邻居。奇数行向右偏移，因此奇偶行的对角邻居列号不同。
        /// </summary>
        private static IEnumerable<(int Col, int Row)> HexNeighbors(int col, int row)
        {
            if ((row & 1) == 0)
            {
                // 偶数行
                yield return (col + 1, row);     // E
                yield return (col - 1, row);     // W
                yield return (col, row - 1);      // NE
                yield return (col - 1, row - 1); // NW
                yield return (col, row + 1);      // SE
                yield return (col - 1, row + 1); // SW
            }
            else
            {
                // 奇数行
                yield return (col + 1, row);     // E
                yield return (col - 1, row);     // W
                yield return (col + 1, row - 1); // NE
                yield return (col, row - 1);      // NW
                yield return (col + 1, row + 1); // SE
                yield return (col, row + 1);      // SW
            }
        }
    }
}
