using System;
using System.Collections.Generic;
using System.Drawing;

namespace WplaceColorWatch
{

public sealed class RuntimeState
{
    private readonly object _lock = new();

    public BgrColor? CurrentBgr { get; private set; }
    public Point? CurrentPos { get; private set; }
    public List<BgrColor> RecordedBgrs { get; private set; } = new();
    public List<BgrColor> RecordedBgrsRaw { get; private set; } = new();
    public Point? RecordedPos { get; private set; }
    public Rectangle? RecordedRange { get; private set; }
    // 正交多边形顶点（屏幕坐标，闭合环，含自动拐角）；null 表示矩形模式，下游直接用 RecordedRange。
    // 非 null 时 RecordedRange 为该多边形的外接矩形，扫描/填涂/预览只在多边形内部进行。
    public List<Point>? RecordedPolygon { get; private set; }
    public bool ActionEnabled { get; private set; }
    public bool AutoFillEnabled { get; private set; }
    public bool AutoFillPrimed { get; private set; }
    public bool AutoFillReady { get; private set; }
    public List<Point> AutoFillPoints { get; private set; } = new();
    public int AutoFillIndex { get; private set; }
    public long LastActionTicks { get; private set; }
    public int ScanTotal { get; private set; }
    public int ScanDone { get; private set; }
    public DateTime ScanStartTime { get; private set; }
    public DateTime AutoFillStartTime { get; private set; }
    public List<Point> PreviewPoints { get; private set; } = new();
    public bool PreviewReady { get; private set; }

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
            Logger.Debug($"[state] RecordColors raw={raw.Count} match={match.Count} pos=({pos.X},{pos.Y})");
        }
    }

    public void SetRange(Rectangle rect, List<Point>? polygon = null)
    {
        lock (_lock)
        {
            RecordedRange = rect;
            RecordedPolygon = polygon;
            Logger.Debug($"[state] SetRange rect=({rect.X},{rect.Y},{rect.Width},{rect.Height}) polygon={(polygon == null ? "null" : polygon.Count.ToString())}");
        }
    }

    public List<Point>? GetPolygon()
    {
        lock (_lock)
        {
            return RecordedPolygon == null ? null : new List<Point>(RecordedPolygon);
        }
    }

    public bool ToggleAction()
    {
        lock (_lock)
        {
            ActionEnabled = !ActionEnabled;
            Logger.Debug($"[state] ToggleAction enabled={ActionEnabled}");
            return ActionEnabled;
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            Logger.Debug($"[state] StopAll prev: action={ActionEnabled} autoFill={AutoFillEnabled} primed={AutoFillPrimed} ready={AutoFillReady} fillPoints={AutoFillPoints.Count} fillIdx={AutoFillIndex}");
            ActionEnabled = false;
            AutoFillEnabled = false;
            AutoFillPrimed = false;
            AutoFillReady = false;
            AutoFillPoints = new List<Point>();
            AutoFillIndex = 0;
            // 预览范围与采样点保持就绪，仅在 ESC 停止动作，不清除显示状态
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
            AutoFillStartTime = DateTime.Now;
            Logger.Debug($"[state] StartAutoFill enabled={AutoFillEnabled} ready={AutoFillReady}");
        }
    }

    public void StartScan(int total)
    {
        lock (_lock)
        {
            ScanTotal = total;
            ScanDone = 0;
            ScanStartTime = DateTime.Now;
            Logger.Debug($"[state] StartScan total={total}");
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
            Logger.Debug($"[state] SetAutoFillPoints count={points.Count} ready={AutoFillReady}");
        }
    }

    public void SetPreviewPoints(List<Point> points)
    {
        lock (_lock)
        {
            PreviewPoints = points;
            PreviewReady = points.Count > 0;
            Logger.Debug($"[state] SetPreviewPoints count={points.Count} ready={PreviewReady}");
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

    public void SetAutoFillIndex(int index)
    {
        lock (_lock)
        {
            AutoFillIndex = Math.Max(0, Math.Min(index, AutoFillPoints.Count));
        }
    }

    public List<Point> GetAutoFillPoints()
    {
        lock (_lock)
        {
            return new List<Point>(AutoFillPoints);
        }
    }

    public bool IsAutoFillActive()
    {
        lock (_lock)
        {
            return AutoFillEnabled && AutoFillReady && AutoFillIndex < AutoFillPoints.Count;
        }
    }

    public Point? TryTakeNextAutoFillPoint()
    {
        lock (_lock)
        {
            if (!AutoFillEnabled || AutoFillPoints.Count == 0)
            {
                return null;
            }
            if (AutoFillIndex >= AutoFillPoints.Count)
            {
                AutoFillEnabled = false;
                Logger.Debug($"[state] TryTakeNextAutoFillPoint completed (all {AutoFillPoints.Count} points done)");
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
            Logger.Debug($"[state] SetAutoFillPrimed");
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
        int autoFillIndex, int autoFillPointsCount, long lastActionTicks, int scanTotal, int scanDone,
        DateTime scanStartTime, DateTime autoFillStartTime,
        List<Point> previewPoints, bool previewReady) Snapshot()
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
                ScanDone,
                ScanStartTime,
                AutoFillStartTime,
                new List<Point>(PreviewPoints),
                PreviewReady
            );
        }
    }
}
}
