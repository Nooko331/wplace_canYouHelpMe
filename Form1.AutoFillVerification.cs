using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace WplaceColorWatch
{

public partial class Form1
{
    private bool TryPickAutoFillColor(Point point, BgrColor target,
        bool whiteColorCompleted, HashSet<BgrColor> pickedColors, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Logger.Debug($"[exec_all] picking target={FormatBgr(target)} pt=({point.X},{point.Y})");
        Thread.Sleep(_options.ActionDelayMs);
        ClickRightCurrentPosition();
        if (token.WaitHandle.WaitOne(200)) token.ThrowIfCancellationRequested();

        var recorded = PerformColorRecordAndAction(point);
        token.ThrowIfCancellationRequested();
        if (whiteColorCompleted && !IsWhite(target) && IsWhiteSample(recorded.clear))
        {
            Logger.Debug($"[auto_all] skip white sample target={FormatBgr(target)} pt=({point.X},{point.Y}) clear={FormatBgr(recorded.clear)}");
            return false;
        }
        if (IsColorAlreadyPicked(recorded.clear, pickedColors, target))
        {
            Logger.Error($"[auto_all] 取色异常：目标色 {FormatBgr(target)} 在点 ({point.X},{point.Y}) 取到已用过的颜色 {FormatBgr(recorded.clear)}，跳过填涂并移至下一点重新取色");
            return false;
        }
        return true;
    }

    private bool VerifyPaintedPoint(Point point, BgrColor target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var capture = BeginScreenCapture();
        var cursorPosition = Cursor.Position;
        bool movedForVerification = false;
        try
        {
            // Normally the next point checks the paint immediately after a
            // pick. If that picked point is last, move away just for this check.
            if (cursorPosition == point)
            {
                var safe = PickSafePos(_state.Snapshot().recordedRange, point) ?? PickSafePos(null, point);
                if (!safe.HasValue || !NativeMethods.SetCursorPos(safe.Value.X, safe.Value.Y))
                {
                    Logger.Warning($"[auto_all] 无法移开鼠标校验涂色：pt=({point.X},{point.Y}) target={FormatBgr(target)}");
                    return false;
                }
                movedForVerification = true;
            }

            using var dc = new ScreenDc();
            BgrColor? previous = null;
            BgrColor? actual = null;
            int stableReads = 0;
            long started = Environment.TickCount64;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (dc.TryGetPixel(point.X, point.Y, out var sample))
                {
                    stableReads = previous.HasValue && sample.MaxDiff(previous.Value) <= ClearSampleStableTolerance
                        ? stableReads + 1 : 1;
                    if (sample.MaxDiff(target) > _options.ColorTol) stableReads = 0;
                    previous = sample;
                    actual = sample;
                    // Painted pixels must match the opaque target. Accepting a
                    // reference-image display alias would also confirm a missed fill.
                    if (Environment.TickCount64 - started >= ClearSampleMinWaitMs &&
                        stableReads >= ClearSampleStableReads && sample.MaxDiff(target) <= _options.ColorTol)
                    {
                        Logger.Debug($"[auto_all] 涂色校验通过：pt=({point.X},{point.Y}) target={FormatBgr(target)} actual={FormatBgr(sample)}");
                        return true;
                    }
                }
                else
                {
                    previous = null;
                    actual = null;
                    stableReads = 0;
                }

                // Even a stable wrong color may still be awaiting a browser
                // render. Give it the full settling window before repicking.
                if (Environment.TickCount64 - started >= ClearSampleTimeoutMs) break;
                if (token.WaitHandle.WaitOne(ClearSamplePollMs)) token.ThrowIfCancellationRequested();
            }

            string observed = actual.HasValue ? FormatBgr(actual.Value) : "读取失败";
            Logger.Warning($"[auto_all] 涂色校验不符：pt=({point.X},{point.Y}) target={FormatBgr(target)} actual={observed} stableReads={stableReads}，笔色需要重新取色确认");
            return false;
        }
        finally
        {
            if (movedForVerification)
            {
                NativeMethods.SetCursorPos(cursorPosition.X, cursorPosition.Y);
            }
        }
    }
}
}
