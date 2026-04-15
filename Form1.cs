using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WplaceColorWatch
{

public partial class Form1 : Form
{
    private enum UiLayoutMode
    {
        Vertical,
        Horizontal
    }

    private const string RepoUrl = "https://github.com/Nooko331/wplace_canYouHelpMe";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Nooko331/wplace_canYouHelpMe/releases/latest";
    private static readonly Version CurrentAppVersion = new(1, 0, 1);
    private const int DefaultScanWorkers = 1;
    private const int DefaultScanStep = 10;

    private readonly Options _options;
    private readonly uint _showMainWindowMessage;
    private readonly RuntimeState _state = new();
    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _autoAllCts;
    private int _autoAllProgressCurrent;
    private int _autoAllProgressTotal;
    private DateTime _autoAllProgressStart;
    private bool _autoAllProgressActive;
    private UiLayoutMode _layoutMode = UiLayoutMode.Vertical;
    private string _updateTargetUrl = RepoUrl;
    private readonly Panel _compactDivider1 = new();
    private readonly Panel _compactDivider2 = new();

    public Form1(Options options, uint showMainWindowMessage)
    {
        _options = options;
        _showMainWindowMessage = showMainWindowMessage;
        InitializeComponent();
        MaximizeBox = false;
        KeyPreview = true;
        TrySetEnglishInputLanguage();
        labelRec.Parent = panelLeft;
        labelRec.Location = new Point(4, 4);
        labelRec.BackColor = Color.Transparent;
        updateTimer.Interval = _options.IntervalMs;
        updateTimer.Tick += UpdateTimerOnTick;
        updateTimer.Start();

        btnRange.Click += (_, _) => BeginRangeSelect();
        btnFill.Click += (_, _) => BeginFill();
        btnAutoFillAll.Click += (_, _) => BeginAutoFillAll();
        btnAutoCores.Click += (_, _) => AutoDetectCores();
        btnToggleLayout.Click += (_, _) => ToggleLayoutMode();
        linkGithubOrUpdate.LinkClicked += (_, _) => OpenUrl(_updateTargetUrl);
        textCores.Leave += (_, _) => _options.ScanWorkers = ReadScanWorkers();
        ScanStep.Leave += (_, _) => _options.ScanStep = ReadScanStep();
        textCores.KeyDown += NumericTextBoxOnKeyDown;
        ScanStep.KeyDown += NumericTextBoxOnKeyDown;
        textCores.Text = _options.ScanWorkers.ToString();
        ScanStep.Text = _options.ScanStep.ToString();
        linkGithubOrUpdate.Text = "项目仓库（GitHub）";
        _compactDivider1.Width = 2;
        _compactDivider1.BackColor = Color.Black;
        _compactDivider1.Visible = false;
        _compactDivider2.Width = 2;
        _compactDivider2.BackColor = Color.Black;
        _compactDivider2.Visible = false;
        Controls.Add(_compactDivider1);
        Controls.Add(_compactDivider2);
        ApplyLayout(_layoutMode);

        SetHook();
        Shown += (_, _) => BeginInvoke((Action)EnsureVisibleAndActivated);
        Shown += async (_, _) => await CheckLatestReleaseAsync();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        CleanupResources();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MAXIMIZE = 0xF030;
        const int WM_NCLBUTTONDBLCLK = 0x00A3;
        if (m.Msg == _showMainWindowMessage)
        {
            EnsureVisibleAndActivated();
            return;
        }
        if (m.Msg == WM_NCLBUTTONDBLCLK)
        {
            return;
        }
        if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == SC_MAXIMIZE)
        {
            return;
        }
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11)
        {
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Focused)
        {
            EnsureVisibleAndActivated();
        }
    }

    private void EnsureVisibleAndActivated()
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)EnsureVisibleAndActivated);
            return;
        }

        if (IsDisposed)
        {
            return;
        }

        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        NativeMethods.ShowWindow(Handle, NativeMethods.SW_RESTORE);
        Show();
        TopMost = true;
        BringToFront();
        Activate();
        NativeMethods.SetForegroundWindow(Handle);
    }

    private void UpdateTimerOnTick(object? sender, EventArgs e)
    {
        var pos = Cursor.Position;
        var bgr = GetPixelAt(pos.X, pos.Y);
        _state.UpdateCurrent(bgr, pos);

        var snapshot = _state.Snapshot();
        // 鐩墠S閿嚜鍔ㄦ粦鍔ㄦ娴嬬簿搴︽瀬鍏朵笉鍑嗙‘锛屾殏鏃跺叧闂?
        bool match = false;
        // bool isOverSelf = IsCursorOverSelf(pos);
        // int minDiff = int.MaxValue;
        // int bestIndex = -1;
        // if (!isOverSelf && snapshot.currentBgr.HasValue && snapshot.recordedBgrs.Count > 0)
        // {
        //     var current = snapshot.currentBgr.Value;
        //     for (int i = 0; i < snapshot.recordedBgrs.Count; i++)
        //     {
        //         var rbgr = snapshot.recordedBgrs[i];
        //         int diff = current.MaxDiff(rbgr);
        //         if (diff < minDiff)
        //         {
        //             minDiff = diff;
        //             bestIndex = i;
        //         }
        //     }
        //     match = minDiff <= _options.ColorTol;
        // }

        // if (_options.Debug && snapshot.actionEnabled && snapshot.currentBgr.HasValue && snapshot.recordedBgrs.Count > 0)
        // {
        //     var now = Environment.TickCount64;
        //     bool stateChanged = match != _lastMatch;
        //     if (stateChanged || now - _lastMatchDebugTicks >= 1000)
        //     {
        //         var current = snapshot.currentBgr.Value;
        //         var best = bestIndex >= 0 ? FormatBgr(snapshot.recordedBgrs[bestIndex]) : "n/a";
        //         var minText = bestIndex >= 0 ? minDiff.ToString() : "n/a";
        //         Logger.Debug($"[match] enabled={snapshot.actionEnabled} self={isOverSelf} match={match} tol={_options.ColorTol} min_diff={minText} current={FormatBgr(current)} best={best} colors={FormatBgrs(snapshot.recordedBgrs)}");
        //         _lastMatchDebugTicks = now;
        //         _lastMatch = match;
        //     }
        // }

        // if (snapshot.actionEnabled && match)
        // {
        //     TryFireSpace();
        // }

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

    private DateTime _autoFillAllStartTime;

    private string GetEta(DateTime startTime, int current, int total)
    {
        if (current <= 0 || total <= 0 || current >= total) return "";
        var elapsed = DateTime.Now - startTime;
        if (elapsed.TotalSeconds < 1) return "";
        var rate = current / elapsed.TotalSeconds;
        var remaining = total - current;
        if (rate <= 0) return "";
        var etaSeconds = remaining / rate;
        var eta = TimeSpan.FromSeconds(etaSeconds);
        return $" ({eta:mm\\:ss})";
    }

    private void UpdateUi((BgrColor? currentBgr, Point? currentPos, List<BgrColor> recordedBgrs,
        List<BgrColor> recordedBgrsRaw, Point? recordedPos, Rectangle? recordedRange,
        bool actionEnabled, bool autoFillEnabled, bool autoFillPrimed, bool autoFillReady,
        int autoFillIndex, int autoFillPointsCount, long lastActionTicks, int scanTotal, int scanDone,
        DateTime scanStartTime, DateTime autoFillStartTime) snapshot, bool match)
    {
        if (snapshot.recordedBgrsRaw.Count > 0)
        {
            panelLeft.BackColor = snapshot.recordedBgrsRaw[0].ToColor();
            panelRight.BackColor = snapshot.recordedBgrsRaw.Count > 1
                ? snapshot.recordedBgrsRaw[1].ToColor()
                : snapshot.recordedBgrsRaw[0].ToColor();
            var left = snapshot.recordedBgrsRaw[0];
            var right = snapshot.recordedBgrsRaw.Count > 1 ? snapshot.recordedBgrsRaw[1] : left;
            color1.Text = $"RGB: {left.R},{left.G},{left.B}";
            color2.Text = $"RGB: {right.R},{right.G},{right.B}";
        }
        else
        {
            color1.Text = "当前记录颜色1";
            color2.Text = "当前记录颜色2";
        }
        // 鐩墠S閿嚜鍔ㄦ粦鍔ㄦ娴嬬簿搴︽瀬鍏朵笉鍑嗙‘锛屾殏鏃跺叧闂?
        // labelMatch.Visible = match;
        // labelX.Text = snapshot.actionEnabled ? "S:ON" : "S:OFF";
        // labelX.ForeColor = snapshot.actionEnabled ? Color.Green : Color.Red;
        if (snapshot.recordedRange.HasValue)
        {
            RangeRecord.ForeColor = Color.Green;
            RangeRecord.Text = "已记录";
            var rect = snapshot.recordedRange.Value;
            TheRange.Text = $"{rect.X},{rect.Y},{rect.Width},{rect.Height}";
        }
        else
        {
            RangeRecord.ForeColor = Color.Red;
            RangeRecord.Text = "未记录";
            TheRange.Text = "0";
        }

        bool scanRunning = _scanCts != null || _autoAllCts != null;
        bool matchRunning = snapshot.autoFillEnabled && snapshot.autoFillReady;

        var scanTotal = Math.Max(1, snapshot.scanTotal);
        progressScan.Maximum = scanTotal;
        progressScan.Value = Math.Min(snapshot.scanDone, scanTotal);
        var scanEta = scanRunning ? GetEta(snapshot.scanStartTime, snapshot.scanDone, snapshot.scanTotal) : "";
        labelScanValue.Text = $"{snapshot.scanDone} / {snapshot.scanTotal}{scanEta}";

        var matchTotal = Math.Max(1, snapshot.autoFillPointsCount);
        progressMatch.Maximum = matchTotal;
        progressMatch.Value = Math.Min(snapshot.autoFillIndex, matchTotal);
        var matchEta = matchRunning ? GetEta(snapshot.autoFillStartTime, snapshot.autoFillIndex, snapshot.autoFillPointsCount) : "";
        labelMatchValue.Text = $"{snapshot.autoFillIndex} / {snapshot.autoFillPointsCount}{matchEta}";

        if (_layoutMode == UiLayoutMode.Horizontal)
        {
            int total = 0;
            int current = 0;
            DateTime start = DateTime.Now;
            bool isRunning = false;
            if (_autoAllProgressTotal > 0 && (_autoAllProgressActive || _autoAllProgressCurrent > 0))
            {
                total = _autoAllProgressTotal;
                current = _autoAllProgressCurrent;
                start = _autoAllProgressStart;
                isRunning = _autoAllProgressActive;
            }
            else if (snapshot.autoFillPointsCount > 0)
            {
                total = snapshot.autoFillPointsCount;
                current = snapshot.autoFillIndex;
                start = snapshot.autoFillStartTime;
                isRunning = matchRunning;
            }
            else if (snapshot.scanTotal > 0)
            {
                total = snapshot.scanTotal;
                current = snapshot.scanDone;
                start = snapshot.scanStartTime;
                isRunning = scanRunning;
            }

            progressAutoAll.Maximum = Math.Max(1, total);
            progressAutoAll.Value = Math.Min(Math.Max(0, current), progressAutoAll.Maximum);
            var totalEta = isRunning ? GetEta(start, current, total) : "";
            labelAutoAllValue.Text = $"{current} / {total}{totalEta}";
        }
    }

    private void TrySetEnglishInputLanguage()
    {
        var target = InputLanguage.InstalledInputLanguages
            .Cast<InputLanguage>()
            .FirstOrDefault(lang => string.Equals(lang.Culture.Name, "en-US", StringComparison.OrdinalIgnoreCase))
            ?? InputLanguage.InstalledInputLanguages
                .Cast<InputLanguage>()
                .FirstOrDefault(lang => string.Equals(lang.Culture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            InputLanguage.CurrentInputLanguage = target;
            if (_options.Debug)
            {
                Logger.Debug($"[ime] set input lang={target.Culture.Name}");
            }
        }
        else if (_options.Debug)
        {
            Logger.Debug("[ime] no English input language installed");
        }
    }

    private static string FormatBgr(BgrColor bgr)
    {
        return $"{bgr.R},{bgr.G},{bgr.B}";
    }

    private static string FormatBgrs(List<BgrColor> bgrs)
    {
        if (bgrs.Count == 0)
        {
            return "[]";
        }
        var parts = new string[bgrs.Count];
        for (int i = 0; i < bgrs.Count; i++)
        {
            parts[i] = FormatBgr(bgrs[i]);
        }
        return $"[{string.Join(" | ", parts)}]";
    }

    private bool IsCursorOverSelf(Point pos)
    {
        return Bounds.Contains(pos);
    }

    private void BeginRangeSelect()
    {
        _options.ScanStep = ReadScanStep();
        if (ScanStep.Text != _options.ScanStep.ToString())
        {
            ScanStep.Text = _options.ScanStep.ToString();
        }
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
        _options.ScanStep = ReadScanStep();
        if (ScanStep.Text != _options.ScanStep.ToString())
        {
            ScanStep.Text = _options.ScanStep.ToString();
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
                BeginInvoke((Action)(() =>
                {
                    btnFill.Enabled = true;
                    var cts = Interlocked.Exchange(ref _scanCts, null);
                    cts?.Dispose();
                }));
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
        _state.StartScan(total);
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
                int row = y / _options.ScanStep;
                int startOffset = (row % 2 == 1) ? (_options.ScanStep / 2) : 0;
                for (int x = startOffset; x < width; x += _options.ScanStep)
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
                        int startOffset = (row % 2 == 1) ? (_options.ScanStep / 2) : 0;
                        for (int x = startOffset; x < width; x += _options.ScanStep)
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
        if (int.TryParse(textCores.Text, out var value) && value > 0 && value <= maxWorkers)
        {
            return value;
        }

        textCores.Text = DefaultScanWorkers.ToString();
        ShowInvalidInputMessage();
        return DefaultScanWorkers;
    }

    private int ReadScanStep()
    {
        if (int.TryParse(ScanStep.Text, out var value) && value > 0)
        {
            return value;
        }

        ScanStep.Text = DefaultScanStep.ToString();
        ShowInvalidInputMessage();
        return DefaultScanStep;
    }

    private static void ShowInvalidInputMessage()
    {
        MessageBox.Show("输入内容无效。", "提示");
    }

    private void NumericTextBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        if (ReferenceEquals(sender, textCores))
        {
            _options.ScanWorkers = ReadScanWorkers();
        }
        else if (ReferenceEquals(sender, ScanStep))
        {
            _options.ScanStep = ReadScanStep();
        }

        e.SuppressKeyPress = true;
        e.Handled = true;
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

    private void ClickRightCurrentPosition()
    {
        Thread.Sleep(_options.ActionDelayMs);
        mouse_event(0x08, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_RIGHTDOWN
        mouse_event(0x10, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_RIGHTUP
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
        CancelAutoAll();
        Unhook();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == NativeMethods.VK_ESCAPE)
            {
                BeginInvoke((Action)(() =>
                {
                    _state.StopAll();
                    CancelScan();
                    CancelAutoAll();
                }));
            }
            // 鐩墠S閿嚜鍔ㄦ粦鍔ㄦ娴嬬簿搴︽瀬鍏朵笉鍑嗙‘锛屾殏鏃跺叧闂?
            // else if (vkCode == NativeMethods.VK_S)
            // {
            //     BeginInvoke((Action)(() =>
            //     {
            //         bool enabled = _state.ToggleAction();
            //         Logger.Debug($"[toggle] enabled={enabled}");
            //     }));
            // }
            else if (vkCode == NativeMethods.VK_A)
            {
                BeginInvoke((Action)(() => PerformColorRecordAndAction()));
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void PerformColorRecordAndAction()
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
        // FocusTargetUnderCursor(); // Removed as requested
        Logger.Debug("[action] send key I");
        Thread.Sleep(50);
        SendKey(NativeMethods.VK_I);
        Thread.Sleep(50);
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
        }
        Thread.Sleep(_options.ActionDelayMs);
    }

    private void CancelAutoAll()
    {
        _autoAllProgressActive = false;
        var cts = Interlocked.Exchange(ref _autoAllCts, null);
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void BeginAutoFillAll()
    {
        if (!btnAutoFillAll.Enabled) return;

        _options.ScanStep = ReadScanStep();
        if (ScanStep.Text != _options.ScanStep.ToString())
        {
            ScanStep.Text = _options.ScanStep.ToString();
        }

        var snapshot = _state.Snapshot();
        if (!snapshot.recordedRange.HasValue)
        {
            MessageBox.Show("请先框选检测范围", "提示");
            return;
        }

        var htmlColors = GetPredefinedColors();
        if (htmlColors.Count == 0)
        {
             MessageBox.Show("未找到颜色定义", "错误");
             return;
        }
        Logger.Debug($"[auto_all] found {htmlColors.Count} colors");

        var workers = ReadScanWorkers();
        _options.ScanWorkers = workers;
        if (textCores.Text != workers.ToString())
        {
            textCores.Text = workers.ToString();
        }

        btnAutoFillAll.Enabled = false;
        btnFill.Enabled = false;
        btnRange.Enabled = false;
        
        CancelScan();
        CancelAutoAll();
        _autoAllCts = new CancellationTokenSource();
        var token = _autoAllCts.Token;
        _autoAllProgressCurrent = 0;
        _autoAllProgressTotal = 0;
        _autoAllProgressStart = DateTime.Now;
        _autoAllProgressActive = true;

        Task.Run(() =>
        {
            try
            {
                Logger.Debug("[auto_all] scanning...");
                var groups = ScanMatchingPointsForAllColors(snapshot.recordedRange.Value, htmlColors, token);
                if (token.IsCancellationRequested) return;

                int totalPoints = groups.Values.Sum(list => list.Count);
                Logger.Debug($"[auto_all] scan done. total_points={totalPoints}");
                
                BeginInvoke((Action)(() => {
                    _autoAllProgressCurrent = 0;
                    _autoAllProgressTotal = totalPoints;
                    _autoAllProgressStart = DateTime.Now;
                    _autoAllProgressActive = true;
                    if (_layoutMode != UiLayoutMode.Horizontal)
                    {
                        progressAutoAll.Maximum = totalPoints;
                        progressAutoAll.Value = 0;
                        labelAutoAllValue.Text = $"0 / {totalPoints}";
                    }
                }));

                ExecuteAutoFillAll(groups, htmlColors, token);
            }
            catch (Exception ex)
            {
                Logger.Debug($"[auto_all] error: {ex}");
            }
            finally
            {
                BeginInvoke((Action)(() =>
                {
                    _autoAllProgressActive = false;
                    btnAutoFillAll.Enabled = true;
                    btnFill.Enabled = true;
                    btnRange.Enabled = true;
                    var cts = Interlocked.Exchange(ref _autoAllCts, null);
                    cts?.Dispose();
                }));
            }
        }, token);
    }

    private List<BgrColor> GetPredefinedColors()
    {
        return new List<BgrColor>
        {
            new BgrColor(0, 0, 0),       // Black
            new BgrColor(60, 60, 60),    // Dark Gray
            new BgrColor(120, 120, 120), // Gray
            new BgrColor(170, 170, 170), // Medium Gray
            new BgrColor(210, 210, 210), // Light Gray
            new BgrColor(255, 255, 255), // White
            new BgrColor(24, 0, 96),     // Deep Red (BGR: 24, 0, 96) -> RGB: 96, 0, 24
            new BgrColor(30, 14, 165),   // Dark Red (BGR: 30, 14, 165) -> RGB: 165, 14, 30
            new BgrColor(36, 28, 237),   // Red (BGR: 36, 28, 237) -> RGB: 237, 28, 36
            new BgrColor(114, 128, 250), // Light Red (BGR: 114, 128, 250) -> RGB: 250, 128, 114
            new BgrColor(26, 92, 228),   // Dark Orange (BGR: 26, 92, 228) -> RGB: 228, 92, 26
            new BgrColor(39, 127, 255),  // Orange (BGR: 39, 127, 255) -> RGB: 255, 127, 39
            new BgrColor(9, 170, 246),   // Gold (BGR: 9, 170, 246) -> RGB: 246, 170, 9
            new BgrColor(59, 221, 249),  // Yellow (BGR: 59, 221, 249) -> RGB: 249, 221, 59
            new BgrColor(188, 250, 255), // Light Yellow (BGR: 188, 250, 255) -> RGB: 255, 250, 188
            new BgrColor(49, 132, 156),  // Dark Goldenrod (BGR: 49, 132, 156) -> RGB: 156, 132, 49
            new BgrColor(49, 173, 197),  // Goldenrod (BGR: 49, 173, 197) -> RGB: 197, 173, 49
            new BgrColor(95, 212, 232),  // Light Goldenrod (BGR: 95, 212, 232) -> RGB: 232, 212, 95
            new BgrColor(58, 107, 74),   // Dark Olive (BGR: 58, 107, 74) -> RGB: 74, 107, 58
            new BgrColor(74, 148, 90),   // Olive (BGR: 74, 148, 90) -> RGB: 90, 148, 74
            new BgrColor(115, 197, 132), // Light Olive (BGR: 115, 197, 132) -> RGB: 132, 197, 115
            new BgrColor(104, 185, 14),  // Dark Green (BGR: 104, 185, 14) -> RGB: 14, 185, 104
            new BgrColor(123, 230, 19),  // Green (BGR: 123, 230, 19) -> RGB: 19, 230, 123
            new BgrColor(94, 255, 135),  // Light Green (BGR: 94, 255, 135) -> RGB: 135, 255, 94
            new BgrColor(110, 129, 12),  // Dark Teal (BGR: 110, 129, 12) -> RGB: 12, 129, 110
            new BgrColor(166, 174, 16),  // Teal (BGR: 166, 174, 16) -> RGB: 16, 174, 166
            new BgrColor(190, 225, 19),  // Light Teal (BGR: 190, 225, 19) -> RGB: 19, 225, 190
            new BgrColor(159, 121, 15),  // Dark Cyan (BGR: 159, 121, 15) -> RGB: 15, 121, 159
            new BgrColor(242, 247, 96),  // Cyan (BGR: 242, 247, 96) -> RGB: 96, 247, 242
            new BgrColor(242, 250, 187), // Light Cyan (BGR: 242, 250, 187) -> RGB: 187, 250, 242
            new BgrColor(158, 80, 40),   // Dark Blue (BGR: 158, 80, 40) -> RGB: 40, 80, 158
            new BgrColor(228, 147, 64),  // Blue (BGR: 228, 147, 64) -> RGB: 64, 147, 228
            new BgrColor(255, 199, 125), // Light Blue (BGR: 255, 199, 125) -> RGB: 125, 199, 255
            new BgrColor(184, 49, 77),   // Dark Indigo (BGR: 184, 49, 77) -> RGB: 77, 49, 184
            new BgrColor(246, 80, 107),  // Indigo (BGR: 246, 80, 107) -> RGB: 107, 80, 246
            new BgrColor(251, 177, 153), // Light Indigo (BGR: 251, 177, 153) -> RGB: 153, 177, 251
            new BgrColor(132, 66, 74),   // Dark Slate Blue (BGR: 132, 66, 74) -> RGB: 74, 66, 132
            new BgrColor(196, 113, 122), // Slate Blue (BGR: 196, 113, 122) -> RGB: 122, 113, 196
            new BgrColor(241, 174, 181), // Light Slate Blue (BGR: 241, 174, 181) -> RGB: 181, 174, 241
            new BgrColor(153, 12, 120),  // Dark Purple (BGR: 153, 12, 120) -> RGB: 120, 12, 153
            new BgrColor(185, 56, 170),  // Purple (BGR: 185, 56, 170) -> RGB: 170, 56, 185
            new BgrColor(249, 159, 224), // Light Purple (BGR: 249, 159, 224) -> RGB: 224, 159, 249
            new BgrColor(122, 0, 203),   // Dark Pink (BGR: 122, 0, 203) -> RGB: 203, 0, 122
            new BgrColor(128, 31, 236),  // Pink (BGR: 128, 31, 236) -> RGB: 236, 31, 128
            new BgrColor(169, 141, 243), // Light Pink (BGR: 169, 141, 243) -> RGB: 243, 141, 169
            new BgrColor(73, 82, 155),   // Dark Peach (BGR: 73, 82, 155) -> RGB: 155, 82, 73
            new BgrColor(120, 128, 209), // Peach (BGR: 120, 128, 209) -> RGB: 209, 128, 120
            new BgrColor(164, 182, 250), // Light Peach (BGR: 164, 182, 250) -> RGB: 250, 182, 164
            new BgrColor(52, 70, 104),   // Dark Brown (BGR: 52, 70, 104) -> RGB: 104, 70, 52
            new BgrColor(42, 104, 149),  // Brown (BGR: 42, 104, 149) -> RGB: 149, 104, 42
            new BgrColor(99, 164, 219),  // Light Brown (BGR: 99, 164, 219) -> RGB: 219, 164, 99
            new BgrColor(82, 99, 123),   // Dark Tan (BGR: 82, 99, 123) -> RGB: 123, 99, 82
            new BgrColor(107, 132, 156), // Tan (BGR: 107, 132, 156) -> RGB: 156, 132, 107
            new BgrColor(148, 181, 214), // Light Tan (BGR: 148, 181, 214) -> RGB: 214, 181, 148
            new BgrColor(81, 128, 209),  // Dark Beige (BGR: 81, 128, 209) -> RGB: 209, 128, 81
            new BgrColor(119, 178, 248), // Beige (BGR: 119, 178, 248) -> RGB: 248, 178, 119
            new BgrColor(165, 197, 255), // Light Beige (BGR: 165, 197, 255) -> RGB: 255, 197, 165
            new BgrColor(63, 100, 109),  // Dark Stone (BGR: 63, 100, 109) -> RGB: 109, 100, 63
            new BgrColor(107, 140, 148), // Stone (BGR: 107, 140, 148) -> RGB: 148, 140, 107
            new BgrColor(158, 197, 205), // Light Stone (BGR: 158, 197, 205) -> RGB: 205, 197, 158
            new BgrColor(65, 57, 51),    // Dark Slate (BGR: 65, 57, 51) -> RGB: 51, 57, 65
            new BgrColor(141, 117, 109), // Slate (BGR: 141, 117, 109) -> RGB: 109, 117, 141
            new BgrColor(209, 185, 179)  // Light Slate (BGR: 209, 185, 179) -> RGB: 179, 185, 209
        };
    }

    private Dictionary<BgrColor, List<Point>> ScanMatchingPointsForAllColors(Rectangle rect, List<BgrColor> targets, CancellationToken token)
    {
        var groups = new ConcurrentDictionary<BgrColor, ConcurrentBag<Point>>();
        foreach(var c in targets) groups[c] = new ConcurrentBag<Point>();

        int width = Math.Max(1, rect.Width);
        int height = Math.Max(1, rect.Height);
        int countX = ((width - 1) / _options.ScanStep) + 1;
        int countY = ((height - 1) / _options.ScanStep) + 1;
        int total = Math.Max(0, countX * countY);
        _state.StartScan(total);
        int done = 0;
        long lastProgressTicks = Environment.TickCount64;
        
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

        Parallel.For(0, countY, new ParallelOptions { MaxDegreeOfParallelism = _options.ScanWorkers, CancellationToken = token }, (row) =>
        {
            int y = row * _options.ScanStep;
            if (y >= height) return;
            
            int startOffset = (row % 2 == 1) ? (_options.ScanStep / 2) : 0;
            for (int x = startOffset; x < width; x += _options.ScanStep)
            {
                if (token.IsCancellationRequested) return;

                int offset = (y * data.Stride) + (x * 4);
                byte b = buffer[offset];
                byte g = buffer[offset + 1];
                byte r = buffer[offset + 2];
                var pixelColor = new BgrColor(b, g, r);

                foreach (var target in targets)
                {
                    if (pixelColor.MaxDiff(target) <= _options.ColorTol)
                    {
                        groups[target].Add(new Point(rect.Left + x, rect.Top + y));
                        break; 
                    }
                }
            }
            var newDone = Interlocked.Add(ref done, countX);
            var now = Environment.TickCount64;
            var prev = Interlocked.Read(ref lastProgressTicks);
            if (now - prev >= 200)
            {
                 if (Interlocked.CompareExchange(ref lastProgressTicks, now, prev) == prev)
                 {
                     _state.SetScanProgress(total, newDone);
                 }
            }
        });
        _state.SetScanProgress(total, done);

        var result = new Dictionary<BgrColor, List<Point>>();
        foreach (var kvp in groups)
        {
            if (kvp.Value.Count > 0)
            {
                result[kvp.Key] = kvp.Value.ToList();
            }
        }
        return result;
    }

    private void ExecuteAutoFillAll(Dictionary<BgrColor, List<Point>> groups, List<BgrColor> order, CancellationToken token)
    {
        _autoFillAllStartTime = DateTime.Now;
        int total = groups.Values.Sum(l => l.Count);
        int processed = 0;

        // Ensure we yield focus away from our form initially
        Thread.Sleep(500); 

        foreach (var color in order)
        {
            if (token.IsCancellationRequested) return;
            if (!groups.ContainsKey(color)) continue;

            var points = groups[color];
            if (points.Count == 0) continue;

            var first = points[0];
            Cursor.Position = first;
            Thread.Sleep(_options.ActionDelayMs);
            
            // Focus on the window under cursor FIRST and wait longer
            ClickRightCurrentPosition();
            Thread.Sleep(200); // Increased delay to ensure focus is applied
            
            PerformColorRecordAndAction();
            if (token.IsCancellationRequested) return;
            Thread.Sleep(50);
            SendSpace();
            processed++;
            UpdateAutoAllProgress(processed, total);

            // Handle subsequent points for this color
            for (int i = 1; i < points.Count; i++)
            {
                if (token.IsCancellationRequested) return;
                Cursor.Position = points[i];
                // FocusTargetUnderCursor(); 
                SendSpace();
                processed++;
                UpdateAutoAllProgress(processed, total);
            }
        }
    }

    private void UpdateAutoAllProgress(int current, int total)
    {
        if (current % 5 == 0 || current == total) 
        {
            BeginInvoke((Action)(() =>
            {
                _autoAllProgressCurrent = current;
                _autoAllProgressTotal = total;
                _autoAllProgressStart = _autoFillAllStartTime;
                _autoAllProgressActive = current < total;
                if (_layoutMode != UiLayoutMode.Horizontal)
                {
                    progressAutoAll.Maximum = total;
                    progressAutoAll.Value = Math.Min(current, total);
                    labelAutoAllValue.Text = $"{current} / {total}{GetEta(_autoFillAllStartTime, current, total)}";
                }
            }));
        }
    }

    private void ToggleLayoutMode()
    {
        _layoutMode = _layoutMode == UiLayoutMode.Vertical ? UiLayoutMode.Horizontal : UiLayoutMode.Vertical;
        ApplyLayout(_layoutMode);
    }

    private void ApplyLayout(UiLayoutMode mode)
    {
        SuspendLayout();
        try
        {
            if (mode == UiLayoutMode.Vertical)
            {
                ClientSize = new Size(383, 670);
                _compactDivider1.Visible = false;
                _compactDivider2.Visible = false;
                color1.Visible = true;
                color2.Visible = true;
                label1.Visible = true;
                label2.Visible = true;
                label4.Visible = true;
                label5.Visible = true;
                label6.Visible = true;
                label8.Visible = true;
                label10.Visible = true;
                TheRange.Visible = true;
                labelScan.Visible = true;
                labelScanValue.Visible = true;
                progressScan.Visible = true;
                labelMatchProgress.Visible = true;
                labelMatchValue.Visible = true;
                progressMatch.Visible = true;
                panelLeft.Location = new Point(12, 62);
                panelLeft.Size = new Size(108, 50);
                panelRight.Location = new Point(133, 62);
                panelRight.Size = new Size(106, 50);
                labelCores.Text = "调用CPU数量";
                btnAutoCores.Text = "自动决定CPU数量";
                label7.Text = "扫描步长";
                btnRange.Text = "划取检测范围";
                btnFill.Text = "自动填充";
                btnAutoFillAll.Text = "全自动检测及填充";
                labelAutoAll.Text = "全自动填充进度";
                color1.Location = new Point(12, 39);
                color2.Location = new Point(133, 39);
                label1.Location = new Point(245, 62);
                label2.Location = new Point(224, 273);
                label3.Location = new Point(12, 9);
                label4.Location = new Point(303, 157);
                label5.Location = new Point(252, 198);
                label6.Location = new Point(12, 127);
                labelCores.Location = new Point(12, 160);
                textCores.Location = new Point(119, 157);
                textCores.Size = new Size(40, 27);
                btnAutoCores.Location = new Point(10, 198);
                btnAutoCores.Size = new Size(142, 26);
                label7.Location = new Point(14, 234);
                ScanStep.Location = new Point(89, 231);
                ScanStep.Size = new Size(69, 27);
                label8.Location = new Point(176, 234);
                btnRange.Location = new Point(12, 273);
                btnRange.Size = new Size(122, 26);
                RangeRecord.Location = new Point(12, 311);
                TheRange.Location = new Point(113, 311);
                btnFill.Location = new Point(12, 341);
                btnFill.Size = new Size(89, 26);
                label10.Location = new Point(167, 341);
                labelScan.Location = new Point(14, 382);
                labelScanValue.Location = new Point(160, 382);
                progressScan.Location = new Point(14, 405);
                progressScan.Size = new Size(351, 28);
                labelMatchProgress.Location = new Point(17, 456);
                labelMatchValue.Location = new Point(160, 456);
                progressMatch.Location = new Point(12, 479);
                progressMatch.Size = new Size(351, 28);
                btnAutoFillAll.Location = new Point(12, 520);
                btnAutoFillAll.Size = new Size(160, 26);
                btnToggleLayout.Location = new Point(218, 637);
                btnToggleLayout.Size = new Size(145, 26);
                labelAutoAll.Location = new Point(17, 560);
                labelAutoAllValue.Location = new Point(160, 560);
                progressAutoAll.Location = new Point(12, 585);
                progressAutoAll.Size = new Size(351, 28);
                linkGithubOrUpdate.Location = new Point(12, 641);
                btnToggleLayout.Text = "精简布局";
            }
            else
            {
                ClientSize = new Size(700, 180);
                _compactDivider1.Visible = true;
                _compactDivider2.Visible = false;
                color1.Visible = false;
                color2.Visible = false;
                label1.Visible = false;
                label2.Visible = false;
                label4.Visible = false;
                label5.Visible = false;
                label6.Visible = false;
                label8.Visible = false;
                label10.Visible = false;
                TheRange.Visible = false;
                labelScan.Visible = false;
                labelScanValue.Visible = false;
                progressScan.Visible = false;
                labelMatchProgress.Visible = false;
                labelMatchValue.Visible = false;
                progressMatch.Visible = false;

                // 区域1：颜色/CPU/步长/划取
                label3.Location = new Point(10, 8);
                panelLeft.Location = new Point(10, 56);
                panelLeft.Size = new Size(108, 50);
                panelRight.Location = new Point(128, 56);
                panelRight.Size = new Size(106, 50);
                color1.Location = new Point(10, 33);
                color2.Location = new Point(128, 33);

                labelCores.Text = "cpu";
                labelCores.Location = new Point(248, 32);
                textCores.Location = new Point(286, 29);
                textCores.Size = new Size(40, 27);
                btnAutoCores.Text = "自动";
                btnAutoCores.Location = new Point(332, 29);
                btnAutoCores.Size = new Size(56, 26);

                label7.Text = "步长";
                label7.Location = new Point(248, 66);
                ScanStep.Location = new Point(286, 63);
                ScanStep.Size = new Size(60, 27);

                btnRange.Text = "划取";
                btnRange.Location = new Point(248, 98);
                btnRange.Size = new Size(58, 26);
                RangeRecord.Location = new Point(312, 102);

                // 区域2：动作按钮 + 进度
                btnFill.Text = "自动";
                btnFill.Location = new Point(430, 20);
                btnFill.Size = new Size(58, 26);

                btnAutoFillAll.Text = "全自动";
                btnAutoFillAll.Location = new Point(494, 20);
                btnAutoFillAll.Size = new Size(76, 26);

                labelAutoAll.Text = "总进度";
                labelAutoAll.Location = new Point(430, 58);
                labelAutoAllValue.Location = new Point(500, 58);
                progressAutoAll.Location = new Point(430, 82);
                progressAutoAll.Size = new Size(250, 22);

                // 区域1底部：入口
                linkGithubOrUpdate.Location = new Point(10, 146);
                btnToggleLayout.Location = new Point(250, 143);
                btnToggleLayout.Size = new Size(104, 26);
                btnToggleLayout.Text = "完整布局";

                _compactDivider1.Location = new Point(410, 16);
                _compactDivider1.Height = 140;
            }
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }
    }

    private async Task CheckLatestReleaseAsync()
    {
        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(6)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("wplace_canYouHelpMe-update-check");
            using var response = await http.GetAsync(LatestReleaseApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);
            if (json.RootElement.TryGetProperty("prerelease", out var prereleaseElem) && prereleaseElem.GetBoolean())
            {
                return;
            }

            if (!json.RootElement.TryGetProperty("tag_name", out var tagElem))
            {
                return;
            }

            var latestTag = tagElem.GetString();
            if (string.IsNullOrWhiteSpace(latestTag) || !TryParseVersion(latestTag, out var latestVersion))
            {
                return;
            }

            if (latestVersion <= CurrentAppVersion)
            {
                return;
            }

            string latestUrl = RepoUrl;
            if (json.RootElement.TryGetProperty("html_url", out var htmlElem))
            {
                var parsed = htmlElem.GetString();
                if (!string.IsNullOrWhiteSpace(parsed))
                {
                    latestUrl = parsed;
                }
            }

            _updateTargetUrl = latestUrl;
            linkGithubOrUpdate.Text = $"发现新版本 {latestTag}，点击下载";
            linkGithubOrUpdate.LinkColor = Color.OrangeRed;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[update] check failed: {ex.Message}");
        }
    }

    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(1);
        }

        var match = Regex.Match(normalized, @"\d+(\.\d+){0,3}");
        if (!match.Success)
        {
            return false;
        }

        var parts = match.Value.Split('.')
            .Select(part => int.TryParse(part, out var n) ? n : 0)
            .ToList();

        while (parts.Count < 4)
        {
            parts.Add(0);
        }

        version = new Version(parts[0], parts[1], parts[2], parts[3]);
        return true;
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if shell open is unavailable.
        }
    }

    private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void btnRange_Click(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void labelMatchProgress_Click(object sender, EventArgs e)
        {

        }

        private void labelMatchValue_Click(object sender, EventArgs e)
        {

        }

        private void label8_Click(object sender, EventArgs e)
        {

        }
    }
}


