using System;
using System.Drawing;
using System.Windows.Forms;

namespace WplaceColorWatch
{

    public sealed class SelectionForm : Form
    {
        private static readonly Color SelectionAccentColor = Color.FromArgb(120, 255, 0, 0);
        private static readonly Color SelectionOuterColor = Color.White;
        private static readonly Color MaskColor = Color.FromArgb(140, 0, 0, 0);
        private readonly Bitmap _screenSnapshot;
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
            _screenSnapshot = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(_screenSnapshot))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 1;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _screenSnapshot.Dispose();
            }
            base.Dispose(disposing);
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
            e.Graphics.DrawImageUnscaled(_screenSnapshot, 0, 0);
            using var maskBrush = new SolidBrush(MaskColor);

            if (!_dragging)
            {
                e.Graphics.FillRectangle(maskBrush, ClientRectangle);
                return;
            }

            var rect = GetRect(_start, _end);

            if (rect.Top > 0)
            {
                e.Graphics.FillRectangle(maskBrush, 0, 0, ClientSize.Width, rect.Top);
            }
            if (rect.Bottom < ClientSize.Height)
            {
                e.Graphics.FillRectangle(maskBrush, 0, rect.Bottom, ClientSize.Width, ClientSize.Height - rect.Bottom);
            }
            if (rect.Left > 0)
            {
                e.Graphics.FillRectangle(maskBrush, 0, rect.Top, rect.Left, rect.Height);
            }
            if (rect.Right < ClientSize.Width)
            {
                e.Graphics.FillRectangle(maskBrush, rect.Right, rect.Top, ClientSize.Width - rect.Right, rect.Height);
            }

            using var outerPen = new Pen(SelectionOuterColor, 3);
            using var innerPen = new Pen(SelectionAccentColor, 1);
            e.Graphics.DrawRectangle(outerPen, rect);
            e.Graphics.DrawRectangle(innerPen, rect);
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
            using var brush = new SolidBrush(SelectionAccentColor);
            const int dotSize = 5;
            foreach (var pt in ScanPattern.GetGridPoints(rect, _scanStep))
            {
                graphics.FillEllipse(brush, pt.X - 1, pt.Y - 1, dotSize, dotSize);
            }
        }
    }
}

