using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WplaceColorWatch
{
    public sealed class PreviewOverlayForm : Form
    {
        private static readonly Color SelectionOuterColor = Color.White;
        private static readonly Color SelectionAccentColor = Color.FromArgb(180, 255, 0, 0);
        private static readonly Color DotColor = Color.FromArgb(180, 255, 0, 0);

        private Rectangle _range;
        private List<Point> _points = new();
        // 正交多边形轮廓（屏幕坐标，闭合环）；非 null 时画多边形外框替代矩形外框。
        private List<Point>? _polygon;
        private int _startIndex;

        public PreviewOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            TransparencyKey = Color.Black;
            Opacity = 1.0;
            DoubleBuffered = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TRANSPARENT = 0x20;
                const int WS_EX_LAYERED = 0x80000;
                const int WS_EX_TOOLWINDOW = 0x80;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void SetData(Rectangle range, List<Point> points, int startIndex = 0, List<Point>? polygon = null)
        {
            _range = range;
            _points = new List<Point>(points);
            _polygon = polygon == null ? null : new List<Point>(polygon);
            _startIndex = Math.Max(0, Math.Min(startIndex, _points.Count));
            var screen = Screen.FromRectangle(range);
            Bounds = screen.Bounds;
            Refresh();
        }

        public void SetStartIndex(int startIndex)
        {
            var newIndex = Math.Max(0, Math.Min(startIndex, _points.Count));
            if (newIndex == _startIndex)
            {
                return;
            }
            _startIndex = newIndex;
            Refresh();
        }

        public void SetRange(Rectangle range, List<Point>? polygon = null)
        {
            _range = range;
            _polygon = polygon == null ? null : new List<Point>(polygon);
            var screen = Screen.FromRectangle(range);
            Bounds = screen.Bounds;
            Refresh();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_range.Width <= 0 || _range.Height <= 0)
            {
                return;
            }

            // The model stores virtual-desktop coordinates, while the overlay
            // paints in client coordinates. Their origins only coincide on a
            // primary monitor at (0, 0). Translate by the actual client origin
            // so secondary monitors also work, including monitors at negative X/Y.
            var clientOrigin = PointToScreen(Point.Empty);
            e.Graphics.TranslateTransform(-clientOrigin.X, -clientOrigin.Y);

            using var outerPen = new Pen(SelectionOuterColor, 3);
            using var innerPen = new Pen(SelectionAccentColor, 1);
            if (_polygon != null && _polygon.Count >= 2)
            {
                var pts = new Point[_polygon.Count + 1];
                for (int i = 0; i < _polygon.Count; i++)
                {
                    pts[i] = _polygon[i];
                }
                pts[_polygon.Count] = _polygon[0]; // 闭合回起点
                e.Graphics.DrawLines(outerPen, pts);
                e.Graphics.DrawLines(innerPen, pts);
            }
            else
            {
                e.Graphics.DrawRectangle(outerPen, _range);
                e.Graphics.DrawRectangle(innerPen, _range);
            }

            if (_points.Count == 0 || _startIndex >= _points.Count)
            {
                return;
            }

            using var dotBrush = new SolidBrush(DotColor);
            const int dotSize = 3;
            for (int i = _startIndex; i < _points.Count; i++)
            {
                var pt = _points[i];
                e.Graphics.FillRectangle(dotBrush, pt.X - 1, pt.Y - 1, dotSize, dotSize);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 背景完全透明，不绘制任何内容
        }
    }
}
