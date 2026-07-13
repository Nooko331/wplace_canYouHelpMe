using System;
using System.Collections.Generic;
using System.Drawing;

namespace WplaceColorWatch
{
    public static class ScanPattern
    {
        /// <summary>
        /// 在 rect 范围内按品字形（奇数行偏移 step/2，即六边形采样）生成采样网格点。
        /// 当 polygon 非 null 时，仅保留落在正交多边形内部的点（用于多边形框选模式）。
        /// </summary>
        public static List<Point> GetGridPoints(Rectangle rect, int step, List<Point>? polygon = null)
        {
            var points = new List<Point>();
            if (rect.Width <= 0 || rect.Height <= 0 || step <= 0)
            {
                return points;
            }

            int safeStep = Math.Max(1, step);
            OrthogonalPolygon? poly = (polygon != null && polygon.Count >= 3) ? new OrthogonalPolygon(polygon) : null;
            for (int y = rect.Top; y <= rect.Bottom; y += safeStep)
            {
                int row = (y - rect.Top) / safeStep;
                int startOffset = (row % 2 == 1) ? (safeStep / 2) : 0;
                for (int x = rect.Left + startOffset; x <= rect.Right; x += safeStep)
                {
                    if (poly != null && !poly.Contains(x, y))
                    {
                        continue;
                    }
                    points.Add(new Point(x, y));
                }
            }

            return points;
        }
    }
}
