using System;
using System.Windows.Forms;

namespace WplaceColorWatch
{
    public partial class Form1
    {
        private readonly ReleaseNotesHistory _releaseNotesHistory;
        private ReleaseNotesDialog? _releaseNotesDialog;

        private void ShowCurrentReleaseNotes(bool automatically)
        {
            if (IsDisposed || Disposing || _releaseNotesDialog != null) return;
            if (automatically && !_releaseNotesHistory.ShouldShow(_currentVersionText)) return;

            try
            {
                using var dialog = new ReleaseNotesDialog(_currentVersionText, ReleaseNotes.Load())
                {
                    TopMost = TopMost
                };
                bool shown = false;
                dialog.Shown += (_, _) => shown = true;
                _releaseNotesDialog = dialog;
                dialog.ShowDialog(this);
                // 确定、Esc 和右上角关闭均算查看；未能显示时不写入记录。
                if (shown) _releaseNotesHistory.MarkViewed(_currentVersionText);
            }
            catch (Exception ex)
            {
                Logger.Error($"[release-notes] show failed: {ex}");
            }
            finally
            {
                _releaseNotesDialog = null;
            }
        }
    }
}
