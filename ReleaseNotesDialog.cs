using System.Drawing;
using System.Windows.Forms;

namespace WplaceColorWatch
{
    internal sealed class ReleaseNotesDialog : Form
    {
        public ReleaseNotesDialog(string version, string notes)
        {
            Text = $"更新说明 · v{version}";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.White;
            ClientSize = new Size(580, 520);
            MinimumSize = new Size(400, 340);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(24, 20, 24, 18)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label
            {
                Text = "看看这次更新了什么",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 40, 52),
                Margin = new Padding(0, 0, 0, 6)
            };
            var subtitle = new Label
            {
                Text = $"wplace_canYouHelpMe  /  v{version}",
                AutoSize = true,
                ForeColor = Color.FromArgb(64, 103, 168),
                Margin = new Padding(0, 0, 0, 18)
            };
            var content = new RichTextBox
            {
                Name = "releaseNotesContent",
                AccessibleName = "本版更新内容",
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(48, 56, 68),
                ReadOnly = true,
                DetectUrls = false,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Text = notes,
                Margin = new Padding(0, 0, 0, 16),
                TabIndex = 0
            };
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var hint = new Label
            {
                Text = "每个版本仅自动显示一次。\n以后可点击主窗口的“更新说明”查看。",
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 0, 12, 0)
            };
            var close = new Button
            {
                Name = "closeReleaseNotes",
                Text = "知道了",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                MinimumSize = new Size(100, 36),
                Anchor = AnchorStyles.Right,
                UseVisualStyleBackColor = true,
                Margin = Padding.Empty,
                TabIndex = 0
            };
            footer.Controls.Add(hint, 0, 0);
            footer.Controls.Add(close, 1, 0);
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(subtitle, 0, 1);
            layout.Controls.Add(content, 0, 2);
            layout.Controls.Add(footer, 0, 3);
            Controls.Add(layout);
            AcceptButton = close;
            CancelButton = close;
            Shown += (_, _) => close.Focus();
        }
    }
}
