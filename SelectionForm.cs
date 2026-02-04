using System;
using System.Drawing;
using System.Windows.Forms;

namespace WplaceColorWatch
{

    public sealed class SelectionForm : Form
    {
        private bool _dragging;
        private Point _start;
        private Point _end;
        private Rectangle _bounds;
        private readonly int _scanStep;
        public Rectangle? SelectedRect { get; private set; }

        public SelectionForm(Rectangle bounds, int scanStep)
        {
            _bounds = bounds;
            _scanStep = Math.Max(1, scanStep);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.5;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                SelectedRect = null;
                Close();
                return;
            }
            _dragging = true;
            _start = e.Location;
            _end = e.Location;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
            {
                return;
            }
            _end = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging)
            {
                return;
            }
            _dragging = false;
            _end = e.Location;
            var rect = GetRect(_start, _end);
            if (rect.Width < 3 || rect.Height < 3)
            {
                SelectedRect = null;
                return;
            }
            SelectedRect = new Rectangle(
                rect.X + _bounds.Left,
                rect.Y + _bounds.Top,
                rect.Width,
                rect.Height
            );
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                SelectedRect = null;
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_dragging)
            {
                return;
            }
            var rect = GetRect(_start, _end);
            using var pen = new Pen(Color.Red, 8);
            e.Graphics.DrawRectangle(pen, rect);
            DrawScanPoints(e.Graphics, rect);
        }

        private static Rectangle GetRect(Point a, Point b)
        {
            int x1 = Math.Min(a.X, b.X);
            int y1 = Math.Min(a.Y, b.Y);
            int x2 = Math.Max(a.X, b.X);
            int y2 = Math.Max(a.Y, b.Y);
            return Rectangle.FromLTRB(x1, y1, x2, y2);
        }

        private void DrawScanPoints(Graphics graphics, Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }
            using var brush = new SolidBrush(Color.Red);
            const int dotSize = 5;
            for (int y = rect.Top; y <= rect.Bottom; y += _scanStep)
            {
                int row = (y - rect.Top) / _scanStep;
                int startOffset = (row % 2 == 1) ? (_scanStep / 2) : 0;
                for (int x = rect.Left + startOffset; x <= rect.Right; x += _scanStep)
                {
                    graphics.FillEllipse(brush, x - 1, y - 1, dotSize, dotSize);
                }
            }
        }
    }
}

