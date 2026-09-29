using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace WplaceColorWatch
{

internal static class AutoFillColorGroup
{
    // Keep input and screen access in the caller so this sequence can also run
    // against a test canvas without sending real drawing keys or clicks.
    internal static bool Execute(
        IReadOnlyList<Point> points,
        Action<Point> moveToPoint,
        Func<Point, bool> verifyPaintedPoint,
        Func<Point, bool> tryPickColor,
        Action<bool> fillPoint,
        Action pointProcessed,
        CancellationToken token,
        bool verifyAfterPick = true)
    {
        bool needsColorPick = true;
        bool anyPointFilled = false;
        Point? pendingPickedPoint = null;

        foreach (var point in points)
        {
            token.ThrowIfCancellationRequested();
            moveToPoint(point);
            token.ThrowIfCancellationRequested();

            // Only the first paint after picking needs confirmation, unless
            // the caller exempts this group (white). Once it matches, reuse
            // the brush for the rest of this color group.
            if (pendingPickedPoint.HasValue)
            {
                needsColorPick = !verifyPaintedPoint(pendingPickedPoint.Value);
                token.ThrowIfCancellationRequested();
                pendingPickedPoint = null;
            }

            if (needsColorPick)
            {
                bool picked = tryPickColor(point);
                token.ThrowIfCancellationRequested();
                if (!picked)
                {
                    pointProcessed();
                    continue;
                }
            }

            fillPoint(needsColorPick);
            pendingPickedPoint = needsColorPick && verifyAfterPick ? point : null;
            needsColorPick = false;
            anyPointFilled = true;
            pointProcessed();
        }

        // A final point only needs checking if it just picked a color. Ordinary
        // trailing points of an already confirmed brush do not need a check.
        if (pendingPickedPoint.HasValue)
        {
            token.ThrowIfCancellationRequested();
            verifyPaintedPoint(pendingPickedPoint.Value);
            token.ThrowIfCancellationRequested();
        }
        return anyPointFilled;
    }
}
}
