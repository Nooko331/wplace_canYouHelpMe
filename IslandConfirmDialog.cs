using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WplaceColorWatch
{
    /// <summary>
    /// 遗漏检测确认对话框：以模态遮罩形式覆盖主窗体，居中显示信息，
    /// 背景半透明+模糊遮罩盖住后面的界面，防止误点。
    /// </summary>
    public sealed class IslandConfirmDialog : Form
    {
        private readonly Form _owner;
        private readonly bool _showYesNo;

        /// <summary>对话框关闭后的结果：YesNo 模式下为 Yes/No/None；OK 模式下为 OK/None。</summary>
        public DialogResult CustomResult { get; private set; } = DialogResult.None;

        /// <summary>
        /// 显示对话框并阻塞至用户关闭。
        /// </summary>
        /// <param name="owner">所属主窗体（遮罩覆盖其区域）</param>
        /// <param name="message">正文</param>
        /// <param name="title">标题</param>
        /// <param name="showYesNo">true=Yes/No 按钮（确认补涂用）；false=仅 OK 按钮</param>
        public static DialogResult Show(Form owner, string message, string title, bool showYesNo)
        {
            using var dlg = new IslandConfirmDialog(owner, message, title, showYesNo);
            dlg.ShowDialog(owner);
            return dlg.CustomResult;
        }

        private IslandConfirmDialog(Form owner, string message, string title, bool showYesNo)
        {
            _owner = owner;
            _showYesNo = showYesNo;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            ControlBox = false;
            TopMost = true;
            DoubleBuffered = true;

            // 覆盖整个主窗体区域（含标题栏与阴影）
            var bounds = owner.Bounds;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(bounds.X, bounds.Y);
            Size = new Size(bounds.Width, bounds.Height);

            BuildUi(message, title);
        }

        private void BuildUi(string message, string title)
        {
            // 主体卡片：居中，半透明白底
            var card = new Panel
            {
                BackColor = Color.FromArgb(245, 245, 245),
                Size = new Size(360, 0), // 高度由内容撑开，下方计算
                Location = new Point(0, 0),
            };

            var lblTitle = new Label
            {
                Text = title,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 40, 40),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(20, 16),
                Size = new Size(320, 28),
            };

            var lblMessage = new Label
            {
                Text = message,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = Color.FromArgb(60, 60, 60),
                AutoSize = false,
                Location = new Point(20, 52),
                Size = new Size(320, 0), // 高度下方计算
            };

            // 用 TextRenderer 测量正文所需高度
            int textHeight = TextRenderer.MeasureText(message, lblMessage.Font, new Size(320, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            lblMessage.Size = new Size(320, textHeight);

            int cardHeight = 52 + textHeight + 20 /*间距*/ + 44 /*按钮*/ + 16 /*下边距*/;
            card.Size = new Size(360, cardHeight);
            card.Location = new Point((ClientSize.Width - card.Width) / 2, (ClientSize.Height - card.Height) / 2);

            // 圆角边框靠 OnPaint 绘制；按钮
            Button? btnYes = null, btnNo = null, btnOk = null;
            int btnY = cardHeight - 16 - 32;
            if (_showYesNo)
            {
                btnYes = MakeButton("确认补涂", true);
                btnYes.Location = new Point(40, btnY);
                btnYes.Click += (_, _) => { CustomResult = DialogResult.Yes; Close(); };

                btnNo = MakeButton("取消", false);
                btnNo.Location = new Point(200, btnY);
                btnNo.Click += (_, _) => { CustomResult = DialogResult.No; Close(); };

                card.Controls.Add(btnYes);
                card.Controls.Add(btnNo);
            }
            else
            {
                btnOk = MakeButton("知道了", true);
                btnOk.Location = new Point((card.Width - 120) / 2, btnY);
                btnOk.Click += (_, _) => { CustomResult = DialogResult.OK; Close(); };
                card.Controls.Add(btnOk);
            }

            card.Controls.Add(lblTitle);
            card.Controls.Add(lblMessage);
            card.Paint += (s, e) =>
            {
                using var path = RoundedRectPath(new Rectangle(0, 0, card.Width, card.Height), 12);
                using var pen = new Pen(Color.FromArgb(210, 210, 210), 1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawPath(pen, path);
            };

            // 主体面板需用 Region 裁出圆角，避免 BackColor 溢出
            using (var path = RoundedRectPath(new Rectangle(0, 0, card.Width, card.Height), 12))
            {
                card.Region = new Region(path);
            }

            Controls.Add(card);
        }

        private Button MakeButton(string text, bool primary)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                Size = new Size(120, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(0, 120, 215) : Color.FromArgb(240, 240, 240),
                ForeColor = primary ? Color.White : Color.FromArgb(60, 60, 60),
                Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRectPath(Rectangle rect, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 模糊遮罩：深色半透明背景盖住主窗体，防止误点。用线性渐变模拟“模糊”观感。
            using var brush = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 背景由 OnPaint 统一绘制遮罩，这里不画
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x80000;
                const int WS_EX_TOOLWINDOW = 0x80;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // 设置整窗透明度，使遮罩呈半透明效果（卡片本身用不透明色）
            Opacity = 0.97;
        }
    }
}
