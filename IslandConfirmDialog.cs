using System;
using System.Drawing;
using System.Windows.Forms;

namespace WplaceColorWatch
{
    /// <summary>
    /// 与主界面风格一致的确认/提示对话框。
    /// 使用标准 WinForms 固定边框对话框样式，避免与主窗体的默认控件风格不一致。
    /// </summary>
    public sealed class IslandConfirmDialog : Form
    {
        private readonly bool _showYesNo;
        private readonly string? _dontShowAgainText;
        private CheckBox? _dontShowAgainCheckBox;

        /// <summary>
        /// 显示对话框并阻塞至用户关闭。
        /// </summary>
        /// <param name="owner">所属主窗体</param>
        /// <param name="message">正文</param>
        /// <param name="title">标题</param>
        /// <param name="showYesNo">true=是/否 按钮；false=仅确定按钮</param>
        public static DialogResult Show(Form owner, string message, string title, bool showYesNo)
        {
            using var dlg = new IslandConfirmDialog(owner, message, title, showYesNo, null);
            return dlg.ShowDialog(owner);
        }

        /// <summary>
        /// 显示带“不再提示”勾选框的是/否对话框并阻塞至用户关闭。
        /// </summary>
        /// <param name="owner">所属主窗体</param>
        /// <param name="message">正文</param>
        /// <param name="title">标题</param>
        /// <param name="dontShowAgainText">勾选框文案（非空时才显示该勾选框）</param>
        /// <returns>对话框结果 + 勾选框是否被勾选</returns>
        public static (DialogResult result, bool dontShowAgain) ShowWithDontShowAgain(
            Form owner, string message, string title, string dontShowAgainText)
        {
            using var dlg = new IslandConfirmDialog(owner, message, title, true, dontShowAgainText);
            var r = dlg.ShowDialog(owner);
            return (r, dlg._dontShowAgainCheckBox?.Checked ?? false);
        }

        private IslandConfirmDialog(Form owner, string message, string title, bool showYesNo, string? dontShowAgainText)
        {
            _showYesNo = showYesNo;
            _dontShowAgainText = dontShowAgainText;

            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = true;
            TopMost = owner.TopMost;
            Text = title;
            Font = owner.Font;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.Control;

            BuildUi(message);
        }

        private void BuildUi(string message)
        {
            const int marginX = 20;
            const int messageWidth = 360;

            var lblMessage = new Label
            {
                Text = message,
                AutoSize = false,
                Location = new Point(marginX, 18),
                Size = new Size(messageWidth, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false,
            };

            int textHeight = TextRenderer.MeasureText(
                message,
                lblMessage.Font,
            new Size(messageWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            lblMessage.Size = new Size(messageWidth, textHeight);

            int cursorY = lblMessage.Bottom;

            // “不再提示”勾选框（可选）
            if (!string.IsNullOrEmpty(_dontShowAgainText))
            {
                _dontShowAgainCheckBox = new CheckBox
                {
                    Text = _dontShowAgainText,
                    AutoSize = true,
                    Location = new Point(marginX, cursorY + 10),
                    UseVisualStyleBackColor = true,
                };
                Controls.Add(_dontShowAgainCheckBox);
                cursorY = _dontShowAgainCheckBox.Bottom;
            }

            int buttonY = cursorY + 18;
            int formHeight = buttonY + 46;

            ClientSize = new Size(400, formHeight);

            if (_showYesNo)
            {
                var btnYes = new Button
                {
                    Text = "是(&Y)",
                    DialogResult = DialogResult.Yes,
                    Size = new Size(90, 28),
                    UseVisualStyleBackColor = true,
                };
                var btnNo = new Button
                {
                    Text = "否(&N)",
                    DialogResult = DialogResult.No,
                    Size = new Size(90, 28),
                    UseVisualStyleBackColor = true,
                };

                btnYes.Location = new Point(ClientSize.Width - 210, buttonY);
                btnNo.Location = new Point(ClientSize.Width - 110, buttonY);

                Controls.Add(btnYes);
                Controls.Add(btnNo);

                AcceptButton = btnYes;
                CancelButton = btnNo;
            }
            else
            {
                var btnOk = new Button
                {
                    Text = "确定(&O)",
                    DialogResult = DialogResult.OK,
                    Size = new Size(90, 28),
                    UseVisualStyleBackColor = true,
                };

                btnOk.Location = new Point(ClientSize.Width - 110, buttonY);
                Controls.Add(btnOk);

                AcceptButton = btnOk;
                CancelButton = btnOk;
            }

            Controls.Add(lblMessage);
        }
    }
}
