using System;
using System.Drawing;
using System.Threading;

namespace WplaceColorWatch
{

public partial class Form1
{
    // Only the UI thread reads/writes this depth. Nested sampling must not restore
    // the overlay before the surrounding eyedropper/click action has finished.
    private int _overlayCaptureDepth;
    private bool _closing;

    private bool IsPreviewOverlaySuppressed =>
        _overlayCaptureDepth > 0 || IsColorRulePicking || _selectingRange || _closing;

    private IDisposable BeginScreenCapture()
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            throw new OperationCanceledException("The capture window is no longer available.");
        }

        void BeginOnUiThread()
        {
            if (_closing || IsDisposed || Disposing)
            {
                throw new OperationCanceledException("The capture window is closing.");
            }

            _overlayCaptureDepth++;
            try
            {
                if (_overlayCaptureDepth == 1)
                {
                    HidePreviewOverlay();
                    // Hiding a window and presenting the desktop are separate
                    // operations. Flush on the thread that changed its visibility.
                    int result = NativeMethods.DwmFlush();
                    if (result < 0)
                    {
                        Logger.Debug($"[overlay] DwmFlush failed hr=0x{result:X8}; waiting before capture");
                        Thread.Sleep(ClearSampleMinWaitMs);
                    }
                }
            }
            catch
            {
                EndScreenCapture();
                throw;
            }
        }

        if (InvokeRequired)
        {
            Invoke((Action)BeginOnUiThread);
        }
        else
        {
            BeginOnUiThread();
        }
        return new OverlayCaptureScope(this);
    }

    private void EndScreenCapture()
    {
        void EndOnUiThread()
        {
            if (_overlayCaptureDepth == 0) return;
            _overlayCaptureDepth--;
            if (_overlayCaptureDepth == 0 && !_closing && !IsDisposed && !Disposing)
            {
                // Keep the current fill order/start index; rebuilding the range
                // preview here would bring already processed red dots back.
                UpdateOverlayFromState(_state.Snapshot());
            }
        }

        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try
        {
            if (InvokeRequired)
            {
                Invoke((Action)EndOnUiThread);
            }
            else
            {
                EndOnUiThread();
            }
        }
        catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated)
        {
            // The user may close the window while the worker releases its scope.
        }
    }

    private void ShowPreviewOverlayIfAllowed()
    {
        // Callers already validated the range. Avoid copying the full point
        // snapshot on every fill-progress update just to decide visibility.
        if (IsPreviewOverlaySuppressed || !checkShowRange.Checked ||
            ((_autoAllCts != null || _islandCts != null) && !_overlayFillMode))
        {
            HidePreviewOverlay();
            return;
        }

        if (_previewOverlay != null && !_previewOverlay.IsDisposed && !_previewOverlay.Visible)
        {
            _previewOverlay.Show(this);
        }
    }

    private void CopyScreenRegion(Bitmap bitmap, Point origin)
    {
        using var capture = BeginScreenCapture();
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(origin, Point.Empty, bitmap.Size, CopyPixelOperation.SourceCopy);
    }

    private sealed class OverlayCaptureScope : IDisposable
    {
        private Form1? _owner;

        public OverlayCaptureScope(Form1 owner) => _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndScreenCapture();
    }
}
}
