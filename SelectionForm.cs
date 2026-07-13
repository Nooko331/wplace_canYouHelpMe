using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace WplaceColorWatch
{

    public sealed class SelectionForm : Form
    {
        private static readonly Color SelectionAccentColor = Color.FromArgb(120, 255, 0, 0);
        private static readonly Color SelectionOuterColor = Color.White;
        private static readonly Color MaskColor = Color.FromArgb(140, 0, 0, 0);
        private const int DragThreshold = 5;      // 按下后移动超过此距离才判定为拖拽矩形
        private const int CloseSnapPx = 10;       // 距首顶点在此范围内点击即闭合多边形

        private readonly Bitmap _screenSnapshot;
        private bool _dragging;
        private Point _start;
        private Point _end;
        private Rectangle _bounds;
        private readonly int _scanStep;

        // 多边形画线模式
        private bool _polygonMode;
        private bool _pressing;            // 首次按下后的“待定”阶段（尚未区分矩形/多边形）
        private Point _pressStart;
        private readonly List<Point> _polygon = new(); // 已提交顶点（本地坐标，含自动拐角）
        private Point _hover;             // 当前鼠标位置（本地坐标）
        private bool _nearClose;         // 鼠标靠近首顶点（可闭合）

        public Rectangle? SelectedRect { get; private set; }
        // 正交多边形顶点（屏幕坐标，闭合环）；null 表示矩形(拖拽)模式。
        public List<Point>? SelectedPolygon { get; private set; }

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
            // 窗体优先接收键盘事件，保证 ESC 在任意阶段（拖拽矩形 / 多边形画线 / 待定）都能立即触发退出
            KeyPreview = true;
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
            // 多边形模式：右键撤销上一个顶点。撤销后若已无顶点，则退出划取区域范围功能。
            if (e.Button == MouseButtons.Right && _polygonMode)
            {
                if (_polygon.Count > 0)
                {
                    _polygon.RemoveAt(_polygon.Count - 1);
                    if (_polygon.Count == 0)
                    {
                        // 屏幕上已无顶点（如：放下首点后即右键撤销），退出划取区域范围功能
                        SelectedRect = null;
                        SelectedPolygon = null;
                        Close();
                        return;
                    }
                    _nearClose = _polygon.Count >= 2 && DistanceSq(_hover, _polygon[0]) <= CloseSnapPx * CloseSnapPx;
                    Invalidate();
                }
                return;
            }
            if (e.Button != MouseButtons.Left)
            {
                // 中键 / 矩形阶段右键：取消整个选择
                SelectedRect = null;
                SelectedPolygon = null;
                Close();
                return;
            }
            if (_polygonMode)
            {
                HandlePolygonClick(e.Location);
                return;
            }
            // 待定阶段：记录按下点，后续按是否发生拖动决定矩形/多边形
            _pressing = true;
            _pressStart = e.Location;
            _start = e.Location;
            _end = e.Location;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_polygonMode)
            {
                _hover = e.Location;
                _nearClose = _polygon.Count >= 2 && DistanceSq(e.Location, _polygon[0]) <= CloseSnapPx * CloseSnapPx;
                Invalidate();
                return;
            }
            if (_dragging)
            {
                _end = e.Location;
                Invalidate();
                return;
            }
            if (_pressing && (e.Button & MouseButtons.Left) != 0)
            {
                if (DistanceSq(e.Location, _pressStart) > DragThreshold * DragThreshold)
                {
                    _dragging = true;
                    _end = e.Location;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                // 矩形拖拽完成
                _dragging = false;
                _pressing = false;
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
                SelectedPolygon = null; // 矩形模式
                Close();
                return;
            }
            if (_pressing)
            {
                // 未拖动 -> 进入多边形模式，置入首个顶点
                _pressing = false;
                _polygonMode = true;
                _hover = _pressStart;
                AddVertex(_pressStart);
                Invalidate();
                return;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            // ESC：无论用哪种方式（矩形拖拽 / 多边形画线 / 待定阶段）都立即退出划取区域范围功能
            if (e.KeyCode == Keys.Escape)
            {
                SelectedRect = null;
                SelectedPolygon = null;
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.DrawImageUnscaled(_screenSnapshot, 0, 0);
            using var maskBrush = new SolidBrush(MaskColor);

            if (_polygonMode)
            {
                PaintPolygon(e.Graphics, maskBrush);
                return;
            }

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

        // ==================== 多边形画线模式 ====================

        private void PaintPolygon(Graphics g, SolidBrush maskBrush)
        {
            // 划取过程中只显示线，不显示范围（不裁剪内部、不画采样点）；
            // 形成闭环后窗体即关闭，范围预览交由 PreviewOverlayForm 负责。
            g.FillRectangle(maskBrush, ClientRectangle);

            // 已提交折线（开放，不含闭合边；每段经 AddVertex 主轴吸附，为单条水平/竖直线）
            if (_polygon.Count >= 2)
            {
                using var outerPen = new Pen(SelectionOuterColor, 3);
                using var innerPen = new Pen(SelectionAccentColor, 1);
                g.DrawLines(outerPen, _polygon.ToArray());
                g.DrawLines(innerPen, _polygon.ToArray());
            }

            // 橡皮筋：末顶点 -> 鼠标，按主轴吸附为单条水平/竖直虚线（非折线）
            if (_polygon.Count >= 1)
            {
                var last = _polygon[_polygon.Count - 1];
                var end = AxisConstrainedPoint(last, _hover);
                using var bandPen = new Pen(SelectionAccentColor, 1) { DashStyle = DashStyle.Dash };
                g.DrawLine(bandPen, last, end);
            }

            // 顶点标记
            using var vtxBrush = new SolidBrush(SelectionOuterColor);
            foreach (var v in _polygon)
            {
                g.FillEllipse(vtxBrush, v.X - 3, v.Y - 3, 6, 6);
            }

            // 首顶点：靠近时高亮提示可闭合
            if (_polygon.Count >= 1)
            {
                var f = _polygon[0];
                if (_nearClose)
                {
                    using var hintPen = new Pen(Color.Lime, 2);
                    g.DrawEllipse(hintPen, f.X - CloseSnapPx, f.Y - CloseSnapPx, CloseSnapPx * 2, CloseSnapPx * 2);
                }
                else
                {
                    using var firstBrush = new SolidBrush(SelectionAccentColor);
                    g.FillEllipse(firstBrush, f.X - 4, f.Y - 4, 8, 8);
                }
            }
        }

        private void HandlePolygonClick(Point p)
        {
            if (_polygon.Count >= 2 && DistanceSq(p, _polygon[0]) <= CloseSnapPx * CloseSnapPx)
            {
                ClosePolygon();
                return;
            }
            AddVertex(p);
            Invalidate();
        }

        // 追加顶点：按主轴吸附到上一顶点，使该段为单条水平/竖直线（非折线）。
        // X 距离更大->横线(取鼠标X、last.Y)；Y 距离更大->竖线(取last.X、鼠标Y)。首顶点原样放置。
        private void AddVertex(Point p)
        {
            if (_polygon.Count > 0)
            {
                var last = _polygon[_polygon.Count - 1];
                p = AxisConstrainedPoint(last, p);
                if (p == last)
                {
                    return; // 投影后与上一顶点重合，忽略零长边
                }
            }
            _polygon.Add(p);
        }

        private void ClosePolygon()
        {
            // 保证闭合边正交：末顶点与首顶点对角时补一个拐角
            if (_polygon.Count >= 2)
            {
                var corner = CornerBetween(_polygon[_polygon.Count - 1], _polygon[0]);
                if (corner.HasValue)
                {
                    _polygon.Add(corner.Value);
                }
            }
            var localBounds = OrthogonalPolygon.Bounds(_polygon);
            if (_polygon.Count < 3 || localBounds.Width < 3 || localBounds.Height < 3)
            {
                // 退化（线/点）-> 取消
                SelectedRect = null;
                SelectedPolygon = null;
                Close();
                return;
            }
            // 转屏幕坐标输出
            var screenPoly = new List<Point>(_polygon.Count);
            foreach (var v in _polygon)
            {
                screenPoly.Add(new Point(v.X + _bounds.Left, v.Y + _bounds.Top));
            }
            SelectedPolygon = screenPoly;
            SelectedRect = new Rectangle(
                localBounds.X + _bounds.Left,
                localBounds.Y + _bounds.Top,
                localBounds.Width,
                localBounds.Height);
            Close();
        }

        // a->b 若对角返回拐角点 (b.x, a.y)（先横后竖）；共线(同x或同y)返回 null。
        // 仅用于闭合边：末顶点与首顶点对角时补一个拐角以保持正交。
        private static Point? CornerBetween(Point a, Point b)
        {
            if (a.X == b.X || a.Y == b.Y)
            {
                return null;
            }
            return new Point(b.X, a.Y);
        }

        // 按“主轴吸附”把 target 投影到过 last 的水平/竖直线上：
        // |dx| >= |dy| -> 横线，取 (target.X, last.Y)；否则竖线，取 (last.X, target.Y)。
        private static Point AxisConstrainedPoint(Point last, Point target)
        {
            int dx = Math.Abs(target.X - last.X);
            int dy = Math.Abs(target.Y - last.Y);
            if (dx >= dy)
            {
                return new Point(target.X, last.Y);
            }
            return new Point(last.X, target.Y);
        }

        private static int DistanceSq(Point a, Point b)
        {
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        // ==================== 矩形拖拽模式 ====================

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
