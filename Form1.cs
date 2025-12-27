using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WplaceColorWatch;

public partial class Form1 : Form
{
    private readonly Options _options;
    private readonly RuntimeState _state = new();
    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private CancellationTokenSource? _scanCts;

    public Form1(Options options)
    {
        _options = options;
        InitializeComponent();
        labelRec.Parent = panelLeft;
        labelRec.Location = new Point(4, 4);
        labelRec.BackColor = Color.Transparent;
        updateTimer.Interval = _options.IntervalMs;
        updateTimer.Tick += UpdateTimerOnTick;
        updateTimer.Start();

        btnRange.Click += (_, _) => BeginRangeSelect();
        btnFill.Click += (_, _) => BeginFill();
        btnAutoCores.Click += (_, _) => AutoDetectCores();
        textCores.Text = _options.ScanWorkers.ToString();

        SetHook();
        Shown += (_, _) => SetTopMostNoActivate();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        CleanupResources();
    }

    private void SetTopMostNoActivate()
    {
        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void UpdateTimerOnTick(object? sender, EventArgs e)
    {
        var pos = Cursor.Position;
        var bgr = GetPixelAt(pos.X, pos.Y);
        _state.UpdateCurrent(bgr, pos);

        var snapshot = _state.Snapshot();
        bool match = false;
        foreach (var rbgr in snapshot.recordedBgrs)
        {
            if (snapshot.currentBgr.HasValue && snapshot.currentBgr.Value.MaxDiff(rbgr) <= _options.ColorTol)
            {
                match = true;
                break;
            }
        }

        if (snapshot.actionEnabled && match)
        {
            TryFireSpace();
        }

        if (snapshot.autoFillEnabled && snapshot.autoFillReady && snapshot.recordedRange.HasValue && snapshot.recordedBgrs.Count > 0)
        {
            TryFireAutoFill();
        }

        UpdateUi(snapshot, match);
    }

    private void TryFireSpace()
    {
        var nowTicks = Environment.TickCount64;
        var snapshot = _state.Snapshot();
        if (nowTicks - snapshot.lastActionTicks < _options.CooldownMs)
        {
            return;
        }
        FocusTargetUnderCursor();
        Logger.Debug("[action] send space");
        SendSpace();
        _state.SetLastActionTicks(nowTicks);
    }

    private void TryFireAutoFill()
    {
        var nowTicks = Environment.TickCount64;
        var snapshot = _state.Snapshot();
        if (nowTicks - snapshot.lastActionTicks < _options.CooldownMs)
        {
            return;
        }
        var pt = _state.NextAutoFillPoint();
        if (pt == null)
        {
            if (_options.Debug)
            {
                Logger.Debug("[auto_fill] no points to fire");
            }
            return;
        }
        Cursor.Position = pt.Value;
        if (!snapshot.autoFillPrimed)
        {
            ClickCurrentPosition();
            _state.SetAutoFillPrimed();
        }
        if (_options.Debug)
        {
            Logger.Debug($"[auto_fill] fire pt=({pt.Value.X},{pt.Value.Y})");
        }
        FocusTargetUnderCursor();
        Logger.Debug("[action] send space (auto_fill)");
        SendSpace();
        _state.SetLastActionTicks(nowTicks);
    }

    private void UpdateUi((BgrColor? currentBgr, Point? currentPos, List<BgrColor> recordedBgrs,
        List<BgrColor> recordedBgrsRaw, Point? recordedPos, Rectangle? recordedRange,
        bool actionEnabled, bool autoFillEnabled, bool autoFillPrimed, bool autoFillReady,
        int autoFillIndex, int autoFillPointsCount, long lastActionTicks, int scanTotal, int scanDone) snapshot, bool match)
    {
        if (snapshot.recordedBgrsRaw.Count > 0)
        {
            panelLeft.BackColor = snapshot.recordedBgrsRaw[0].ToColor();
            panelRight.BackColor = snapshot.recordedBgrsRaw.Count > 1
                ? snapshot.recordedBgrsRaw[1].ToColor()
                : snapshot.recordedBgrsRaw[0].ToColor();
        }
        labelMatch.Visible = match;
        labelX.Text = snapshot.actionEnabled ? "X:ON" : "X:OFF";
        labelX.ForeColor = snapshot.actionEnabled ? Color.Green : Color.Red;
        labelRange.Text = snapshot.recordedRange.HasValue ? "R:OK" : "R:--";

        var scanTotal = Math.Max(1, snapshot.scanTotal);
        progressScan.Maximum = scanTotal;
        progressScan.Value = Math.Min(snapshot.scanDone, scanTotal);
        labelScanValue.Text = $"{snapshot.scanDone} / {snapshot.scanTotal}";

        var matchTotal = Math.Max(1, snapshot.autoFillPointsCount);
        progressMatch.Maximum = matchTotal;
        progressMatch.Value = Math.Min(snapshot.autoFillIndex, matchTotal);
        labelMatchValue.Text = $"{snapshot.autoFillIndex} / {snapshot.autoFillPointsCount}";
    }

    private void BeginRangeSelect()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        using var sel = new SelectionForm(screen.Bounds, _options.ScanStep);
        sel.ShowDialog(this);
        if (sel.SelectedRect.HasValue)
        {
            _state.SetRange(sel.SelectedRect.Value);
            Logger.Debug($"[range] done rect={sel.SelectedRect.Value}");
        }
    }

    private void BeginFill()
    {
        if (!btnFill.Enabled)
        {
            return;
        }
        var snapshot = _state.Snapshot();
        if (!snapshot.recordedRange.HasValue || snapshot.recordedBgrs.Count == 0)
        {
            Logger.Debug("[auto_fill] missing range or color");
            return;
        }
        var fillTargets = snapshot.recordedBgrs.Count > 1
            ? new List<BgrColor> { snapshot.recordedBgrs[1] }
            : new List<BgrColor> { snapshot.recordedBgrs[0] };
        var workers = ReadScanWorkers();
        _options.ScanWorkers = workers;
        if (textCores.Text != workers.ToString())
        {
            textCores.Text = workers.ToString();
        }
        _state.StartAutoFill();
        btnFill.Enabled = false;
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        Logger.Debug("[auto_fill] scanning...");
        Task.Run(() =>
        {
            try
            {
                var points = ScanMatchingPoints(snapshot.recordedRange.Value, fillTargets, token);
                if (token.IsCancellationRequested)
                {
                    Logger.Debug("[scan] canceled before apply");
                    return;
                }
                _state.SetAutoFillPoints(points);
                Logger.Debug($"[auto_fill] enabled points={points.Count}");
            }
            catch (Exception ex)
            {
                Logger.Debug($"[scan] failed: {ex}");
            }
            finally
            {
                BeginInvoke(() =>
                {
                    btnFill.Enabled = true;
                    var cts = Interlocked.Exchange(ref _scanCts, null);
                    cts?.Dispose();
                });
            }
        }, token);
    }

    private List<Point> ScanMatchingPoints(Rectangle rect, List<BgrColor> bgrs, CancellationToken token)
    {
        var points = new List<Point>();
        int minDiff = int.MaxValue;
        int maxDiff = 0;
        long sumDiff = 0;
        long count = 0;
        var snapshot = _state.Snapshot();
        int width = Math.Max(1, rect.Width);
        int height = Math.Max(1, rect.Height);
        int countX = ((width - 1) / _options.ScanStep) + 1;
        int countY = ((height - 1) / _options.ScanStep) + 1;
        int total = Math.Max(0, countX * countY);
        _state.SetScanProgress(total, 0);
        long lastProgressTicks = Environment.TickCount64;
        if (_options.Debug)
        {
            Logger.Debug($"[scan] rect={rect} targets={bgrs.Count} tol={_options.ColorTol} step={_options.ScanStep} workers={_options.ScanWorkers}");
            if (snapshot.recordedPos.HasValue)
            {
                Logger.Debug($"[scan] recorded_pos=({snapshot.recordedPos.Value.X},{snapshot.recordedPos.Value.Y}) in_rect={rect.Contains(snapshot.recordedPos.Value)}");
            }
        }

        if (token.IsCancellationRequested)
        {
            return points;
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        var bitmapRect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(bitmapRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] buffer;
        try
        {
            int bytes = Math.Abs(data.Stride) * height;
            buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        BgrColor ReadBufferPixel(int x, int y)
        {
            int offset = (y * data.Stride) + (x * 4);
            byte b = buffer[offset];
            byte g = buffer[offset + 1];
            byte r = buffer[offset + 2];
            return new BgrColor(b, g, r);
        }

        if (_options.Debug && snapshot.recordedPos.HasValue && rect.Contains(snapshot.recordedPos.Value))
        {
            var local = new Point(snapshot.recordedPos.Value.X - rect.Left, snapshot.recordedPos.Value.Y - rect.Top);
            if (local.X >= 0 && local.Y >= 0 && local.X < width && local.Y < height)
            {
                var rb = ReadBufferPixel(local.X, local.Y);
                var diffs = new List<string>();
                foreach (var target in bgrs)
                {
                    diffs.Add($"[{Math.Abs(rb.R - target.R)},{Math.Abs(rb.G - target.G)},{Math.Abs(rb.B - target.B)}]");
                }
                Logger.Debug($"[scan] recorded_pos sample rgb=[{rb.R},{rb.G},{rb.B}] diffs={string.Join(",", diffs)}");
            }
        }

        if (_options.ProbeX.HasValue && _options.ProbeY.HasValue && rect.Contains(new Point(_options.ProbeX.Value, _options.ProbeY.Value)) && _options.Debug)
        {
            var probeLocal = new Point(_options.ProbeX.Value - rect.Left, _options.ProbeY.Value - rect.Top);
            if (probeLocal.X >= 0 && probeLocal.Y >= 0 && probeLocal.X < width && probeLocal.Y < height)
            {
                var pb = ReadBufferPixel(probeLocal.X, probeLocal.Y);
                var diffs = new List<string>();
                foreach (var rb in bgrs)
                {
                    diffs.Add($"[{Math.Abs(pb.R - rb.R)},{Math.Abs(pb.G - rb.G)},{Math.Abs(pb.B - rb.B)}]");
                }
                Logger.Debug($"[scan] probe pos=({_options.ProbeX.Value},{_options.ProbeY.Value}) bgr_rgb=[{pb.R},{pb.G},{pb.B}] diffs={string.Join(",", diffs)}");
            }
        }

        int done = 0;
        void MaybeUpdateProgress(int doneValue)
        {
            var now = Environment.TickCount64;
            var prev = Interlocked.Read(ref lastProgressTicks);
            if (now - prev < 200)
            {
                return;
            }
            if (Interlocked.CompareExchange(ref lastProgressTicks, now, prev) == prev)
            {
                _state.SetScanProgress(total, doneValue);
            }
        }

        if (_options.ScanWorkers <= 1)
        {
            for (int y = 0; y < height; y += _options.ScanStep)
            {
                if (token.IsCancellationRequested)
                {
                    _state.SetScanProgress(total, done);
                    Logger.Debug("[scan] canceled");
                    return points;
                }
                for (int x = 0; x < width; x += _options.ScanStep)
                {
                    if (token.IsCancellationRequested)
                    {
                        _state.SetScanProgress(total, done);
                        Logger.Debug("[scan] canceled");
                        return points;
                    }
                    var bgr = ReadBufferPixel(x, y);
                    int localMin = int.MaxValue;
                    foreach (var rbgr in bgrs)
                    {
                        int diff = bgr.MaxDiff(rbgr);
                        if (diff < localMin)
                        {
                            localMin = diff;
                        }
                    }
                    minDiff = Math.Min(minDiff, localMin);
                    maxDiff = Math.Max(maxDiff, localMin);
                    sumDiff += localMin;
                    count++;
                    if (localMin <= _options.ColorTol)
                    {
                        points.Add(new Point(rect.Left + x, rect.Top + y));
                    }
                    done++;
                    MaybeUpdateProgress(done);
                }
            }
            _state.SetScanProgress(total, done);
        }
        else
        {
            var pointsBag = new ConcurrentBag<Point>();
            var statsLock = new object();
            try
            {
                Parallel.For(0, countY,
                    new ParallelOptions { MaxDegreeOfParallelism = _options.ScanWorkers, CancellationToken = token },
                    () => new ScanStats(),
                    (row, state, local) =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            state.Stop();
                            return local;
                        }
                        int y = row * _options.ScanStep;
                        if (y >= height)
                        {
                            return local;
                        }
                        for (int x = 0; x < width; x += _options.ScanStep)
                        {
                            if (token.IsCancellationRequested)
                            {
                                state.Stop();
                                break;
                            }
                            var bgr = ReadBufferPixel(x, y);
                            int localMin = int.MaxValue;
                            foreach (var rbgr in bgrs)
                            {
                                int diff = bgr.MaxDiff(rbgr);
                                if (diff < localMin)
                                {
                                    localMin = diff;
                                }
                            }
                            local.Min = Math.Min(local.Min, localMin);
                            local.Max = Math.Max(local.Max, localMin);
                            local.Sum += localMin;
                            local.Count++;
                            if (localMin <= _options.ColorTol)
                            {
                                pointsBag.Add(new Point(rect.Left + x, rect.Top + y));
                            }
                        }
                        var newDone = Interlocked.Add(ref done, countX);
                        if (newDone % 100 == 0)
                        {
                            _state.SetScanProgress(total, newDone);
                        }
                        return local;
                    },
                    local =>
                    {
                        lock (statsLock)
                        {
                            minDiff = Math.Min(minDiff, local.Min);
                            maxDiff = Math.Max(maxDiff, local.Max);
                            sumDiff += local.Sum;
                            count += local.Count;
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("[scan] canceled");
            }
            _state.SetScanProgress(total, done);
            points = new List<Point>(pointsBag);
        }

        if (_options.Debug)
        {
            double avg = count == 0 ? -1 : (double)sumDiff / count;
            Logger.Debug($"[scan] step_used={_options.ScanStep} matches={points.Count} min_diff={minDiff} max_diff={maxDiff} avg_diff={avg:F2}");
            if (_options.ColorTol == 0 && minDiff > 0)
            {
                Logger.Debug($"[scan] note: tol=0 and min_diff={minDiff}, no exact matches");
            }
        }

        return points;
    }

    private int ReadScanWorkers()
    {
        int maxWorkers = Math.Max(1, Environment.ProcessorCount - 1);
        if (int.TryParse(textCores.Text, out var value) && value > 0)
        {
            return Math.Min(value, maxWorkers);
        }
        return maxWorkers;
    }

    private void AutoDetectCores()
    {
        int half = Math.Max(1, Environment.ProcessorCount / 2);
        textCores.Text = half.ToString();
        _options.ScanWorkers = half;
        if (_options.Debug)
        {
            Logger.Debug($"[ui] auto cores={half}");
        }
    }

    private void CancelScan()
    {
        var cts = Interlocked.Exchange(ref _scanCts, null);
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private static BgrColor GetPixelAt(int x, int y)
    {
        using var dc = new ScreenDc();
        return dc.GetPixel(x, y);
    }

    private sealed class ScanStats
    {
        public int Min = int.MaxValue;
        public int Max = 0;
        public long Sum = 0;
        public long Count = 0;
    }

    private void SendSpace()
    {
        Thread.Sleep(_options.ActionDelayMs);
        const ushort vk = 0x20;
        SendKey(vk);
    }

    private void SendKey(ushort vk)
    {
        Thread.Sleep(_options.ActionDelayMs);
        uint scan = NativeMethods.MapVirtualKey(vk, 0);
        if (_options.Debug)
        {
            Logger.Debug($"[action] send key vk=0x{vk:X2} scan=0x{scan:X2}");
        }
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)scan,
                        dwFlags = NativeMethods.KEYEVENTF_SCANCODE
                    }
                }
            },
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)scan,
                        dwFlags = NativeMethods.KEYEVENTF_SCANCODE | NativeMethods.KEYEVENTF_KEYUP
                    }
                }
            }
        };
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (_options.Debug)
        {
            if (sent == 0)
            {
                var err = Marshal.GetLastWin32Error();
                Logger.Debug($"[action] SendInput failed err={err}");
            }
            else
            {
                Logger.Debug($"[action] SendInput ok sent={sent}");
            }
        }
    }

    private void ClickCurrentPosition()
    {
        Thread.Sleep(_options.ActionDelayMs);
        mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    private static Point? PickSafePos(Rectangle? avoidRect)
    {
        foreach (var screen in Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            var candidates = new[]
            {
                new Point(bounds.Left + 5, bounds.Top + 5),
                new Point(bounds.Right - 5, bounds.Top + 5),
                new Point(bounds.Left + 5, bounds.Bottom - 5),
                new Point(bounds.Right - 5, bounds.Bottom - 5)
            };
            foreach (var pt in candidates)
            {
                if (avoidRect == null || !avoidRect.Value.Contains(pt))
                {
                    return pt;
                }
            }
        }
        return null;
    }

    private void SetHook()
    {
        _hookProc = HookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(curModule!.ModuleName),
            0);
    }

    private void Unhook()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private void CleanupResources()
    {
        CancelScan();
        Unhook();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == NativeMethods.VK_ESCAPE)
            {
                BeginInvoke(() =>
                {
                    _state.StopAll();
                    CancelScan();
                });
            }
            else if (vkCode == NativeMethods.VK_X)
            {
                BeginInvoke(() =>
                {
                    bool enabled = _state.ToggleAction();
                    Logger.Debug($"[toggle] enabled={enabled}");
                });
            }
            else if (vkCode == NativeMethods.VK_Z)
            {
                BeginInvoke(() => RecordColors());
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void RecordColors()
    {
        var pos = Cursor.Position;
        BgrColor hover;
        BgrColor clear;
        using (var dc = new ScreenDc())
        {
            hover = dc.GetPixel(pos.X, pos.Y);
        }
        var safe = PickSafePos(_state.Snapshot().recordedRange);
        if (safe.HasValue)
        {
            NativeMethods.SetCursorPos(safe.Value.X, safe.Value.Y);
            Thread.Sleep(30);
        }
        using (var dc = new ScreenDc())
        {
            clear = dc.GetPixel(pos.X, pos.Y);
        }
        NativeMethods.SetCursorPos(pos.X, pos.Y);

        _state.RecordColors(new List<BgrColor> { hover, clear }, new List<BgrColor> { hover, clear }, pos);
        Logger.Debug($"[record] raw_colors_rgb=[{string.Join(",", hover.ToRgbArray())}],[{string.Join(",", clear.ToRgbArray())}] pos=({pos.X},{pos.Y})");
        FocusTargetUnderCursor();
        Logger.Debug("[action] send key I");
        SendKey(NativeMethods.VK_I);
        Logger.Debug("[action] click left");
        ClickCurrentPosition();
    }

    private void FocusTargetUnderCursor()
    {
        NativeMethods.GetCursorPos(out var pt);
        var hwnd = NativeMethods.WindowFromPoint(pt);
        if (_options.Debug)
        {
            var fg = NativeMethods.GetForegroundWindow();
            var fgTid = NativeMethods.GetWindowThreadProcessId(fg, out var fgPid);
            Logger.Debug($"[action] fg hwnd={fg} tid={fgTid} pid={fgPid}");
        }
        if (hwnd != IntPtr.Zero)
        {
            var ok = NativeMethods.SetForegroundWindow(hwnd);
            if (_options.Debug)
            {
                var tid = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                Logger.Debug($"[action] focus hwnd={hwnd} tid={tid} pid={pid} ok={ok}");
            }
            Thread.Sleep(_options.ActionDelayMs);
        }
    }
}
