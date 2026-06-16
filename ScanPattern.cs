using System;
using System.Collections.Generic;
using System.Drawing;

namespace WplaceColorWatch
{
    public static class ScanPattern
    {
        public static List<Point> GetGridPoints(Rectangle rect, int step)
        {
            var points = new List<Point>();
            if (rect.Width <= 0 || rect.Height <= 0 || step <= 0)
            {
                return points;
            }

            int safeStep = Math.Max(1, step);
            for (int y = rect.Top; y <= rect.Bottom; y += safeStep)
            {
                int row = (y - rect.Top) / safeStep;
                int startOffset = (row % 2 == 1) ? (safeStep / 2) : 0;
                for (int x = rect.Left + startOffset; x <= rect.Right; x += safeStep)
                {
                    points.Add(new Point(x, y));
                }
            }

            return points;
        }
    }
}
