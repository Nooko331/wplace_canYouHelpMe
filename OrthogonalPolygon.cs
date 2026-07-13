using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace WplaceColorWatch
{
    /// <summary>
    /// 正交多边形（仅含水平/竖直边）的“点是否在内”判定。
    /// 顶点列表为闭合环：第 i 个顶点连到第 (i+1)%n 个顶点，最后一个连回第一个。
    /// 采用经典射线法（PNPOLY，半开区间），对凹正交多边形同样正确；
    /// 水平边对水平射线无贡献，正交多边形天然适用，且对一般多边形也成立。
    /// 进扫描函数时构造一次、内层循环复用，避免每点重建。
    /// </summary>
    public sealed class OrthogonalPolygon
    {
        private readonly Point[] _vertices;

        public OrthogonalPolygon(List<Point> vertices)
        {
            _vertices = vertices?.ToArray() ?? System.Array.Empty<Point>();
        }

        /// <summary>点 (px,py) 是否在多边形内部（含边界附近，射线法判定）。</summary>
        public bool Contains(int px, int py)
        {
            var v = _vertices;
            int n = v.Length;
            if (n < 3)
            {
                return false;
            }
            bool inside = false;
            // PNPOLY: j 始终为 i 的前一顶点（环状）
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point vi = v[i];
                Point vj = v[j];
                // 边是否跨越水平线 y=py（半开区间，规避顶点重复计数）
                if ((vi.Y > py) != (vj.Y > py))
                {
                    // 边与水平线 y=py 的交点 x
                    double x = (vj.X - vi.X) * (double)(py - vi.Y) / (vj.Y - vi.Y) + vi.X;
                    if (px < x)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        public bool Contains(Point p) => Contains(p.X, p.Y);

        /// <summary>多边形外接矩形。</summary>
        public static Rectangle Bounds(List<Point> vertices)
        {
            if (vertices == null || vertices.Count == 0)
            {
                return Rectangle.Empty;
            }
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var p in vertices)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            return Rectangle.FromLTRB(minX, minY, maxX, maxY);
        }
    }
}
