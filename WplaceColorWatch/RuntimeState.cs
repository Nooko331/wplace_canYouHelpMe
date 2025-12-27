using System.Collections.Generic;
using System.Drawing;

namespace WplaceColorWatch;

public sealed class RuntimeState
{
    private readonly object _lock = new();

    public BgrColor? CurrentBgr { get; private set; }
    public Point? CurrentPos { get; private set; }
    public List<BgrColor> RecordedBgrs { get; private set; } = new();
    public List<BgrColor> RecordedBgrsRaw { get; private set; } = new();
    public Point? RecordedPos { get; private set; }
    public Rectangle? RecordedRange { get; private set; }
    public bool ActionEnabled { get; private set; }
    public bool AutoFillEnabled { get; private set; }
    public bool AutoFillPrimed { get; private set; }
    public bool AutoFillReady { get; private set; }
    public List<Point> AutoFillPoints { get; private set; } = new();
    public int AutoFillIndex { get; private set; }
    public long LastActionTicks { get; private set; }
    public int ScanTotal { get; private set; }
    public int ScanDone { get; private set; }

    public void UpdateCurrent(BgrColor bgr, Point pos)
    {
        lock (_lock)
        {
            CurrentBgr = bgr;
            CurrentPos = pos;
        }
    }

    public void RecordColors(List<BgrColor> raw, List<BgrColor> match, Point pos)
    {
        lock (_lock)
        {
            RecordedBgrsRaw = raw;
            RecordedBgrs = match;
            RecordedPos = pos;
        }
    }

    public void SetRange(Rectangle rect)
    {
        lock (_lock)
        {
            RecordedRange = rect;
        }
    }

    public bool ToggleAction()
    {
        lock (_lock)
        {
            ActionEnabled = !ActionEnabled;
            return ActionEnabled;
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            ActionEnabled = false;
            AutoFillEnabled = false;
            AutoFillPrimed = false;
            AutoFillReady = false;
            AutoFillPoints = new List<Point>();
            AutoFillIndex = 0;
        }
    }

    public void StartAutoFill()
    {
        lock (_lock)
        {
            AutoFillEnabled = true;
            AutoFillPrimed = false;
            AutoFillReady = false;
            AutoFillIndex = 0;
            ScanTotal = 0;
            ScanDone = 0;
        }
    }

    public void SetAutoFillPoints(List<Point> points)
    {
        lock (_lock)
        {
            AutoFillPoints = points;
            AutoFillIndex = 0;
            AutoFillPrimed = false;
            AutoFillReady = true;
        }
    }

    public void SetScanProgress(int total, int done)
    {
        lock (_lock)
        {
            ScanTotal = total;
            ScanDone = done;
        }
    }

    public Point? NextAutoFillPoint()
    {
        lock (_lock)
        {
            if (!AutoFillEnabled || AutoFillPoints.Count == 0)
            {
                if (AutoFillReady)
                {
                    AutoFillEnabled = false;
                }
                return null;
            }
            if (AutoFillIndex >= AutoFillPoints.Count)
            {
                AutoFillEnabled = false;
                return null;
            }
            var pt = AutoFillPoints[AutoFillIndex];
            AutoFillIndex++;
            return pt;
        }
    }

    public void SetAutoFillPrimed()
    {
        lock (_lock)
        {
            AutoFillPrimed = true;
        }
    }

    public void SetLastActionTicks(long ticks)
    {
        lock (_lock)
        {
            LastActionTicks = ticks;
        }
    }

    public (BgrColor? currentBgr, Point? currentPos, List<BgrColor> recordedBgrs,
        List<BgrColor> recordedBgrsRaw, Point? recordedPos, Rectangle? recordedRange,
        bool actionEnabled, bool autoFillEnabled, bool autoFillPrimed, bool autoFillReady,
        int autoFillIndex, int autoFillPointsCount, long lastActionTicks, int scanTotal, int scanDone) Snapshot()
    {
        lock (_lock)
        {
            return (
                CurrentBgr,
                CurrentPos,
                new List<BgrColor>(RecordedBgrs),
                new List<BgrColor>(RecordedBgrsRaw),
                RecordedPos,
                RecordedRange,
                ActionEnabled,
                AutoFillEnabled,
                AutoFillPrimed,
                AutoFillReady,
                AutoFillIndex,
                AutoFillPoints.Count,
                LastActionTicks,
                ScanTotal,
                ScanDone
            );
        }
    }
}
