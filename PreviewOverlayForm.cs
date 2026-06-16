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

        public void SetData(Rectangle range, List<Point> points, int startIndex = 0)
        {
            _range = range;
            _points = new List<Point>(points);
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

        public void SetRange(Rectangle range)
        {
            _range = range;
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

            using var outerPen = new Pen(SelectionOuterColor, 3);
            using var innerPen = new Pen(SelectionAccentColor, 1);
            e.Graphics.DrawRectangle(outerPen, _range);
            e.Graphics.DrawRectangle(innerPen, _range);

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
