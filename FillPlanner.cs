using System;
using System.Collections.Generic;
using System.Drawing;

namespace WplaceColorWatch
{
    /// <summary>
    /// 填色规划器：将同色的网格点聚类成连续区域（簇），并对每个簇内的点按最近邻顺序排列，
    /// 使得填色时可以在同色区域上快速"划过"，减少鼠标跳跃和重复取色开销。
    /// </summary>
    public static class FillPlanner
    {
        /// <summary>
        /// 将同色点列表聚类为若干连续区域，每个区域内的点按最近邻路径排列。
        /// </summary>
        /// <param name="points">同色网格点列表</param>
        /// <param name="step">扫描步长（像素），用于计算网格坐标</param>
        /// <param name="maxNeighborDistance">网格距离 ≤ 此值的点属于同一簇</param>
        /// <returns>聚类后的点列表列表，每个子列表是一个连续同色区域</returns>
        public static List<List<Point>> ClusterPoints(List<Point> points, int step, int maxNeighborDistance)
        {
            var result = new List<List<Point>>();
            if (points == null || points.Count == 0)
            {
                return result;
            }

            int safeStep = Math.Max(1, step);
            int safeDist = Math.Max(1, maxNeighborDistance);

            // 构建 grid -> index 映射，使用字典支持稀疏网格
            var gridMap = new Dictionary<long, int>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                long key = ToGridKey(points[i], safeStep);
                gridMap[key] = i;
            }

            // BFS 将相邻点聚类
            var visited = new bool[points.Count];
            var queue = new Queue<int>();

            for (int i = 0; i < points.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                var cluster = new List<Point>();
                queue.Clear();
                queue.Enqueue(i);
                visited[i] = true;

                while (queue.Count > 0)
                {
                    int curIdx = queue.Dequeue();
                    var cur = points[curIdx];
                    cluster.Add(cur);

                    // 检查上下左右（以及更远距离）的邻居
                    foreach (var neighbor in GetNeighbors(cur, safeStep, safeDist))
                    {
                        long nKey = ToGridKey(neighbor, safeStep);
                        if (gridMap.TryGetValue(nKey, out int nIdx) && !visited[nIdx])
                        {
                            visited[nIdx] = true;
                            queue.Enqueue(nIdx);
                        }
                    }
                }

                // 簇内按最近邻路径排序，使得鼠标可以平滑划过
                var ordered = OrderByNearestNeighbor(cluster);
                result.Add(ordered);
            }

            // 将多个簇也按最近邻排序，减少鼠标跨簇跳跃
            result = OrderClustersByNearestNeighbor(result);

            return result;
        }

        /// <summary>
        /// 将聚类后的多个簇按最近邻顺序排列（以第一个簇的第一个点为起点）。
        /// 返回一个扁平化的点列表（保持簇内顺序）。
        /// </summary>
        public static List<Point> FlattenClusters(List<List<Point>> clusters)
        {
            var result = new List<Point>();
            if (clusters == null || clusters.Count == 0)
            {
                return result;
            }
            foreach (var cluster in clusters)
            {
                result.AddRange(cluster);
            }
            return result;
        }

        // ---- 内部方法 ----

        /// <summary>
        /// 获取一个网格点的邻居候选位置。
        /// maxDistance=1 时检查上下左右4方向；maxDistance≥2 时扩展到更远距离。
        /// </summary>
        private static IEnumerable<Point> GetNeighbors(Point p, int step, int maxDistance)
        {
            for (int dy = -maxDistance; dy <= maxDistance; dy++)
            {
                if (dy == 0) continue;
                yield return new Point(p.X, p.Y + dy * step);
            }
            for (int dx = -maxDistance; dx <= maxDistance; dx++)
            {
                if (dx == 0) continue;
                yield return new Point(p.X + dx * step, p.Y);
            }
        }

        /// <summary>
        /// 将屏幕坐标转换为网格 key（基于步长量化）。
        /// 使用 long 编码 (gx, gy) 避免坐标碰撞。
        /// </summary>
        private static long ToGridKey(Point p, int step)
        {
            int gx = p.X / step;
            int gy = p.Y / step;
            // 使用 ((long)gx << 32) | (uint)gy 编码
            return ((long)gx << 32) | (long)(uint)gy;
        }

        /// <summary>
        /// 簇内最近邻路径排序：贪心地从第一个点出发，每次选最近的未访问点。
        /// </summary>
        private static List<Point> OrderByNearestNeighbor(List<Point> cluster)
        {
            if (cluster.Count <= 1)
            {
                return new List<Point>(cluster);
            }

            var ordered = new List<Point>(cluster.Count);
            var used = new bool[cluster.Count];
            ordered.Add(cluster[0]);
            used[0] = true;

            for (int i = 1; i < cluster.Count; i++)
            {
                var last = ordered[ordered.Count - 1];
                int bestIdx = -1;
                int bestDist = int.MaxValue;
                for (int j = 0; j < cluster.Count; j++)
                {
                    if (used[j]) continue;
                    int dx = cluster[j].X - last.X;
                    int dy = cluster[j].Y - last.Y;
                    int dist = dx * dx + dy * dy;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = j;
                    }
                }
                if (bestIdx >= 0)
                {
                    ordered.Add(cluster[bestIdx]);
                    used[bestIdx] = true;
                }
            }

            return ordered;
        }

        /// <summary>
        /// 多簇最近邻排序：以第一个簇的起点为锚点，贪心地选择最近的下一个簇。
        /// </summary>
        private static List<List<Point>> OrderClustersByNearestNeighbor(List<List<Point>> clusters)
        {
            if (clusters.Count <= 1)
            {
                return clusters;
            }

            var result = new List<List<Point>>(clusters.Count);
            var used = new bool[clusters.Count];
            result.Add(clusters[0]);
            used[0] = true;

            for (int i = 1; i < clusters.Count; i++)
            {
                var lastCluster = result[result.Count - 1];
                var anchor = lastCluster[lastCluster.Count - 1]; // 上一个簇的最后一个点
                int bestIdx = -1;
                int bestDist = int.MaxValue;
                for (int j = 0; j < clusters.Count; j++)
                {
                    if (used[j] || clusters[j].Count == 0) continue;
                    // 用簇的第一个点作为代表来计算距离
                    var candidate = clusters[j][0];
                    int dx = candidate.X - anchor.X;
                    int dy = candidate.Y - anchor.Y;
                    int dist = dx * dx + dy * dy;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = j;
                    }
                }
                if (bestIdx >= 0)
                {
                    result.Add(clusters[bestIdx]);
                    used[bestIdx] = true;
                }
            }

            return result;
        }
    }
}
