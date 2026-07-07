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
    private const string LatestReleaseRedirectUrl = "https://github.com/Nooko331/wplace_canYouHelpMe/releases/latest";
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
    private readonly Version _currentAppVersion;
    private readonly string _currentVersionText;
    private readonly Panel _compactDivider1 = new();
    private readonly Panel _compactDivider2 = new();
    private readonly RadioButton radioSpeedBalanced = new();
    private readonly RadioButton radioSpeedExtreme = new();
    private PreviewOverlayForm? _previewOverlay;
    private List<Point> _overlayFillPoints = new();
    private int _overlayFillStartIndex;
    private bool _overlayFillMode;
    private bool _overlayFillIsFullAuto;
    private System.Windows.Forms.Timer? _startupActivationTimer;
    private int _startupActivationAttempts;

    public Form1(Options options, uint showMainWindowMessage)
    {
        _options = options;
        _showMainWindowMessage = showMainWindowMessage;
        InitializeComponent();
        _currentAppVersion = GetCurrentAppVersion();
        _currentVersionText = GetCurrentVersionText(_currentAppVersion);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
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
        checkShowRange.CheckedChanged += (_, _) => ToggleRangePreview(checkShowRange.Checked);
        linkGithubOrUpdate.LinkClicked += (_, _) => OpenUrl(_updateTargetUrl);
        textCores.Leave += (_, _) => _options.ScanWorkers = ReadScanWorkers();
        ScanStep.Leave += (_, _) =>
        {
            _options.ScanStep = ReadScanStep();
            if (checkShowRange.Checked)
            {
                RefreshRangePreview();
            }
        };
        textCores.KeyDown += NumericTextBoxOnKeyDown;
        ScanStep.KeyDown += NumericTextBoxOnKeyDown;
        textCores.Text = _options.ScanWorkers.ToString();
        ScanStep.Text = _options.ScanStep.ToString();
        linkGithubOrUpdate.Text = "项目仓库（GitHub）";
        labelCurrentVersion.Text = $"当前版本: {_currentVersionText}";
        _compactDivider1.Width = 2;
        _compactDivider1.BackColor = Color.Black;
        _compactDivider1.Visible = false;
        _compactDivider2.Width = 2;
        _compactDivider2.BackColor = Color.Black;
        _compactDivider2.Visible = false;
        Controls.Add(_compactDivider1);
        Controls.Add(_compactDivider2);

        // 速度模式选项
        radioSpeedBalanced.Text = "平衡";
        radioSpeedBalanced.Checked = true;
        radioSpeedBalanced.AutoSize = true;
        radioSpeedBalanced.UseVisualStyleBackColor = true;
        radioSpeedExtreme.Text = "极致速度";
        radioSpeedExtreme.Checked = false;
        radioSpeedExtreme.AutoSize = true;
        radioSpeedExtreme.UseVisualStyleBackColor = true;
        radioSpeedBalanced.CheckedChanged += (_, _) =>
        {
            if (radioSpeedBalanced.Checked) ApplySpeedPreset(SpeedPreset.Balanced);
        };
        radioSpeedExtreme.CheckedChanged += (_, _) =>
        {
            if (radioSpeedExtreme.Checked) ApplySpeedPreset(SpeedPreset.Extreme);
        };
        Controls.Add(radioSpeedBalanced);
        Controls.Add(radioSpeedExtreme);
        ApplySpeedPreset(SpeedPreset.Balanced);

        ApplyLayout(_layoutMode);

        SetHook();
        Shown += (_, _) =>
        {
            BeginInvoke((Action)EnsureVisibleAndActivated);
            BeginStartupActivationRetries();
        };
        Shown += async (_, _) => await CheckLatestReleaseAsync();
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

        NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOW);
        NativeMethods.ShowWindow(Handle, NativeMethods.SW_RESTORE);
        Show();
        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        TopMost = false;
        TopMost = true;
        BringToFront();
        Activate();
        NativeMethods.SetForegroundWindow(Handle);
    }

    private void BeginStartupActivationRetries()
    {
        _startupActivationTimer?.Stop();
        _startupActivationTimer?.Dispose();
        _startupActivationAttempts = 0;
        _startupActivationTimer = new System.Windows.Forms.Timer
        {
            Interval = 250
        };
        _startupActivationTimer.Tick += (_, _) =>
        {
            _startupActivationAttempts++;
            EnsureVisibleAndActivated();
            if (_startupActivationAttempts >= 12)
            {
                _startupActivationTimer?.Stop();
                _startupActivationTimer?.Dispose();
                _startupActivationTimer = null;
            }
        };
        _startupActivationTimer.Start();
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
            // 自动填充现在在扫描完成后由后台 Task 直接批量执行
            // 这里仅保持对全自动的兼容，半自动的 trigger 已内聚到 BeginFill
        }

        UpdateUi(snapshot, match);
        UpdateOverlayFromState(snapshot);
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

    private void ExecuteAutoFillPoints(List<Point> points, bool isFullAuto, CancellationToken token)
    {
        Logger.Debug($"[exec_fill] ExecuteAutoFillPoints start: points={points.Count} isFullAuto={isFullAuto}");
        _autoFillAllStartTime = DateTime.Now;
        int total = points.Count;
        int processed = 0;

        if (isFullAuto)
        {
            Thread.Sleep(500);
        }

        // points 已经在调用方完成了聚类排序，这里直接按顺序遍历
        for (int i = 0; i < points.Count; i++)
        {
            if (token.IsCancellationRequested)
            {
                Logger.Debug($"[exec_fill] cancelled at i={i}/{points.Count}");
                return;
            }

            var pt = points[i];
            Cursor.Position = pt;
            _state.SetAutoFillIndex(i + 1);

            if (isFullAuto)
            {
                Logger.Debug($"[exec_fill] full-auto pt {i}/{total} pos=({pt.X},{pt.Y})");
                ClickRightCurrentPosition();
                Thread.Sleep(200);
                var recorded = PerformColorRecordAndAction();
                if (token.IsCancellationRequested) return;

                // 低频更新覆盖层进度：仅每 10 个点或最后一个点时更新，减少 BeginInvoke 对焦点的干扰
                if (processed % 10 == 0 || i == points.Count - 1)
                {
                    BeginInvoke((Action)(() => SetOverlayFillStartIndex(processed)));
                }

                Thread.Sleep(_options.ColorPickToFillDelayMs);
                SendSpace();
            }
            else
            {
                if (i == 0)
                {
                    Logger.Debug($"[exec_fill] semi-auto first click at pt=({pt.X},{pt.Y})");
                    ClickCurrentPosition();
                }
                if (_options.Debug)
                {
                    Logger.Debug($"[auto_fill] fire pt=({pt.X},{pt.Y})");
                }
                FocusTargetUnderCursor();
                Logger.Debug("[action] send space fill (auto_fill)");
                SendSpaceForFill();

                // 低频更新覆盖层进度：仅每 10 个点或最后一个点时更新
                if ((i + 1) % 10 == 0 || i == points.Count - 1)
                {
                    BeginInvoke((Action)(() => SetOverlayFillStartIndex(i + 1)));
                }
            }

            processed++;
            if (isFullAuto)
            {
                UpdateAutoAllProgress(processed, total);
            }

            if (processed % 10 == 0 || processed == total)
            {
                Logger.Debug($"[exec_fill] progress: {processed}/{total}");
            }
        }
        Logger.Debug($"[exec_fill] ExecuteAutoFillPoints done: processed={processed}/{total}");
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
        DateTime scanStartTime, DateTime autoFillStartTime,
        List<Point> previewPoints, bool previewReady) snapshot, bool match)
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
            var rect = snapshot.recordedRange.Value;
            TheRange.Text = $"{rect.X},{rect.Y},{rect.Width},{rect.Height}";
            if (snapshot.previewReady)
            {
                RangeRecord.ForeColor = Color.Green;
                RangeRecord.Text = "已就绪";
            }
            else
            {
                RangeRecord.ForeColor = Color.Green;
                RangeRecord.Text = "已记录";
            }
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

    private static bool IsWhite(BgrColor bgr)
    {
        return bgr.R == Color.White.R && bgr.G == Color.White.G && bgr.B == Color.White.B;
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
        Logger.Debug("[ui] BeginRangeSelect clicked");
        _options.ScanStep = ReadScanStep();
        if (ScanStep.Text != _options.ScanStep.ToString())
        {
            ScanStep.Text = _options.ScanStep.ToString();
        }
        var wasChecked = checkShowRange.Checked;
        Logger.Debug($"[range] overlay_was_checked={wasChecked} previewOverlay_exists={_previewOverlay != null && !_previewOverlay.IsDisposed}");
        // 彻底清除覆盖层，防止旧的框选范围和像素点被 SelectionForm 截图捕获
        if (_previewOverlay != null)
        {
            Logger.Debug("[range] disposing old overlay");
            _previewOverlay.Hide();
            _previewOverlay.Dispose();
            _previewOverlay = null;
        }
        _overlayFillMode = false;
        // 取消勾选以防止 Timer tick 中 UpdateOverlayFromState 在 SelectionForm 期间重建覆盖层
        checkShowRange.Checked = false;
        Logger.Debug("[range] overlay disposed, checkbox unchecked, opening SelectionForm");

        var screen = Screen.FromPoint(Cursor.Position);
        using var sel = new SelectionForm(screen.Bounds, _options.ScanStep);
        sel.ShowDialog(this);
        Logger.Debug($"[range] SelectionForm closed selected={sel.SelectedRect.HasValue}");
        if (sel.SelectedRect.HasValue)
        {
            _state.SetRange(sel.SelectedRect.Value);
            Logger.Debug($"[range] done rect={sel.SelectedRect.Value}");
            if (wasChecked)
            {
                checkShowRange.Checked = true;
                RefreshRangePreview();
            }
        }
        else if (wasChecked)
        {
            Logger.Debug("[range] selection cancelled, restoring overlay");
            checkShowRange.Checked = true;
            RestorePreviewOverlayIfChecked();
        }
    }

    private void RefreshRangePreview()
    {
        var snapshot = _state.Snapshot();
        Logger.Debug($"[overlay] RefreshRangePreview hasRange={snapshot.recordedRange.HasValue} overlayFillMode={_overlayFillMode}");
        if (!snapshot.recordedRange.HasValue)
        {
            Logger.Debug("[overlay] RefreshRangePreview: no range, hiding");
            _state.SetPreviewPoints(new List<Point>());
            _previewOverlay?.Hide();
            _overlayFillMode = false;
            return;
        }
        var rect = snapshot.recordedRange.Value;
        var points = ScanPattern.GetGridPoints(rect, _options.ScanStep);
        _state.SetPreviewPoints(points);
        if (_previewOverlay == null || _previewOverlay.IsDisposed)
        {
            Logger.Debug("[overlay] RefreshRangePreview: creating new overlay form");
            _previewOverlay = new PreviewOverlayForm();
        }
        _overlayFillMode = false;
        _overlayFillPoints = points;
        _overlayFillStartIndex = 0;
        _previewOverlay.SetData(rect, points, 0);
        if (!_previewOverlay.Visible)
        {
            _previewOverlay.Show(this);
            Logger.Debug($"[overlay] RefreshRangePreview: showing overlay with {points.Count} points");
        }
    }

    private void ToggleRangePreview(bool show)
    {
        Logger.Debug($"[overlay] ToggleRangePreview show={show}");
        if (show)
        {
            RefreshRangePreview();
        }
        else
        {
            Logger.Debug("[overlay] ToggleRangePreview: hiding overlay");
            _previewOverlay?.Hide();
            _overlayFillMode = false;
        }
    }

    private void HidePreviewOverlay()
    {
        if (_previewOverlay != null && _previewOverlay.Visible)
        {
            Logger.Debug($"[overlay] HidePreviewOverlay visible={_previewOverlay.Visible}");
        }
        _previewOverlay?.Hide();
    }

    private void RestorePreviewOverlayIfChecked()
    {
        Logger.Debug($"[overlay] RestorePreviewOverlayIfChecked checked={checkShowRange.Checked}");
        if (checkShowRange.Checked)
        {
            RefreshRangePreview();
        }
    }

    private void UpdateOverlayFromState((BgrColor? currentBgr, Point? currentPos, List<BgrColor> recordedBgrs,
        List<BgrColor> recordedBgrsRaw, Point? recordedPos, Rectangle? recordedRange,
        bool actionEnabled, bool autoFillEnabled, bool autoFillPrimed, bool autoFillReady,
        int autoFillIndex, int autoFillPointsCount, long lastActionTicks, int scanTotal, int scanDone,
        DateTime scanStartTime, DateTime autoFillStartTime,
        List<Point> previewPoints, bool previewReady) snapshot)
    {
        if (!checkShowRange.Checked)
        {
            // 未勾选显示，隐藏覆盖层
            if (_previewOverlay != null && _previewOverlay.Visible)
            {
                Logger.Debug("[overlay] UpdateOverlayFromState: checkbox unchecked, hiding");
            }
            _previewOverlay?.Hide();
            _overlayFillMode = false;
            return;
        }

        if (!snapshot.recordedRange.HasValue)
        {
            _previewOverlay?.Hide();
            return;
        }

        // 扫描期间隐藏覆盖层，避免把覆盖层内容扫进去
        // 但如果已经进入填充模式（_overlayFillMode），不要隐藏覆盖层，填充覆盖层由 SetOverlayFillPoints 管理
        if (_scanCts != null && !_overlayFillMode)
        {
            if (_previewOverlay != null && _previewOverlay.Visible)
            {
                Logger.Debug("[overlay] UpdateOverlayFromState: scanCts active, hiding");
            }
            _previewOverlay?.Hide();
            return;
        }

        // 全自动开始但尚未建立进度点列表前也隐藏
        if (_autoAllCts != null && !_overlayFillIsFullAuto)
        {
            Logger.Debug($"[overlay] UpdateOverlayFromState: autoAllCts active, fillIsFullAuto=false, hiding");
            _previewOverlay?.Hide();
            return;
        }

        var range = snapshot.recordedRange.Value;

        // 半自动填充：显示剩余填充点
        if (!_overlayFillIsFullAuto && snapshot.autoFillEnabled && snapshot.autoFillReady)
        {
            var fillPoints = _state.GetAutoFillPoints();
            if (!_overlayFillMode || _overlayFillPoints.Count != fillPoints.Count || !ReferenceEquals(_overlayFillPoints, fillPoints))
            {
                Logger.Debug($"[overlay] UpdateOverlayFromState: semi-auto fill mode overlayFillMode={_overlayFillMode} fillPts={fillPoints.Count} startIdx={snapshot.autoFillIndex}");
                _overlayFillMode = true;
                _overlayFillIsFullAuto = false;
                _overlayFillPoints = fillPoints;
                _overlayFillStartIndex = snapshot.autoFillIndex;
                EnsureOverlay().SetData(range, fillPoints, snapshot.autoFillIndex);
            }
            else if (_overlayFillStartIndex != snapshot.autoFillIndex)
            {
                Logger.Debug($"[overlay] UpdateOverlayFromState: semi-auto fill update startIdx {_overlayFillStartIndex} -> {snapshot.autoFillIndex}");
                _overlayFillStartIndex = snapshot.autoFillIndex;
                _previewOverlay?.SetStartIndex(snapshot.autoFillIndex);
            }
            return;
        }

        // 全自动：由 ExecuteAutoFillAll 直接维护覆盖层，这里只负责保持显示
        if (_overlayFillIsFullAuto)
        {
            if (_previewOverlay != null && _overlayFillMode && !_previewOverlay.Visible)
            {
                Logger.Debug("[overlay] UpdateOverlayFromState: full-auto fill, re-showing overlay");
                _previewOverlay.Show(this);
            }
            return;
        }

        // 预览模式：显示框 + 采样网格点
        if (snapshot.previewReady)
        {
            if (_overlayFillMode)
            {
                Logger.Debug("[overlay] UpdateOverlayFromState: switching from fill to preview mode");
                _overlayFillMode = false;
                _overlayFillStartIndex = 0;
                EnsureOverlay().SetData(range, snapshot.previewPoints, 0);
            }
            else if (_previewOverlay == null || _previewOverlay.IsDisposed)
            {
                Logger.Debug("[overlay] UpdateOverlayFromState: creating overlay for preview mode");
                EnsureOverlay().SetData(range, snapshot.previewPoints, 0);
            }
            else if (!_previewOverlay.Visible)
            {
                Logger.Debug("[overlay] UpdateOverlayFromState: showing preview overlay");
                _previewOverlay.Show(this);
            }
            return;
        }

        Logger.Debug("[overlay] UpdateOverlayFromState: no active mode, hiding");
        _previewOverlay?.Hide();
        _overlayFillMode = false;
    }

    private void SetOverlayFillPoints(List<Point> points, bool fullAuto)
    {
        Logger.Debug($"[overlay] SetOverlayFillPoints count={points.Count} fullAuto={fullAuto} showRangeChecked={checkShowRange.Checked}");
        if (!checkShowRange.Checked)
        {
            Logger.Debug("[overlay] SetOverlayFillPoints: checkbox not checked, skipping");
            return;
        }
        var snapshot = _state.Snapshot();
        if (!snapshot.recordedRange.HasValue)
        {
            Logger.Debug("[overlay] SetOverlayFillPoints: no recorded range, skipping");
            return;
        }
        _overlayFillMode = true;
        _overlayFillIsFullAuto = fullAuto;
        _overlayFillPoints = new List<Point>(points);
        _overlayFillStartIndex = 0;
        EnsureOverlay().SetData(snapshot.recordedRange.Value, _overlayFillPoints, 0);
        Logger.Debug($"[overlay] SetOverlayFillPoints: overlay setup complete");
    }

    private void SetOverlayFillStartIndex(int startIndex)
    {
        if (!_overlayFillMode || !checkShowRange.Checked)
        {
            return;
        }
        _overlayFillStartIndex = startIndex;
        EnsureOverlay().SetStartIndex(startIndex);
    }

    private void ClearOverlayFill()
    {
        Logger.Debug($"[overlay] ClearOverlayFill prevMode={_overlayFillMode} prevFullAuto={_overlayFillIsFullAuto}");
        _overlayFillMode = false;
        _overlayFillIsFullAuto = false;
        _overlayFillPoints = new List<Point>();
        _overlayFillStartIndex = 0;
    }

    private PreviewOverlayForm EnsureOverlay()
    {
        if (_previewOverlay == null || _previewOverlay.IsDisposed)
        {
            Logger.Debug("[overlay] EnsureOverlay: creating new overlay form");
            _previewOverlay = new PreviewOverlayForm();
        }
        if (!_previewOverlay.Visible)
        {
            Logger.Debug("[overlay] EnsureOverlay: showing overlay");
            _previewOverlay.Show(this);
        }
        _previewOverlay.BringToFront();
        return _previewOverlay;
    }

    private void BeginFill()
    {
        Logger.Debug("[ui] BeginFill clicked");
        if (!btnFill.Enabled)
        {
            Logger.Debug("[auto_fill] button disabled, skipping");
            return;
        }
        _options.ScanStep = ReadScanStep();
        if (ScanStep.Text != _options.ScanStep.ToString())
        {
            ScanStep.Text = _options.ScanStep.ToString();
        }
        var snapshot = _state.Snapshot();
        Logger.Debug($"[auto_fill] state: bgrs={snapshot.recordedBgrs.Count} range={snapshot.recordedRange.HasValue} autoFillEnabled={snapshot.autoFillEnabled}");
        if (snapshot.recordedBgrs.Count == 0)
        {
            Logger.Debug("[auto_fill] no colors recorded, showing message box");
            MessageBox.Show("请通过 A 键选取颜色", "提示");
            return;
        }
        if (!snapshot.recordedRange.HasValue)
        {
            Logger.Debug("[auto_fill] no range, showing message box");
            MessageBox.Show("请框选范围", "提示");
            return;
        }
        // 将用户取色标准化到预定义调色板，以避免屏幕取色与游戏实际颜色的微小差异
        var rawTarget = snapshot.recordedBgrs.Count > 1
            ? snapshot.recordedBgrs[1]
            : snapshot.recordedBgrs[0];
        var predefinedColors = GetPredefinedColors();
        BgrColor fillTarget = rawTarget;
        int minDiff = int.MaxValue;
        foreach (var pc in predefinedColors)
        {
            int diff = rawTarget.MaxDiff(pc);
            if (diff < minDiff)
            {
                minDiff = diff;
                fillTarget = pc;
            }
        }
        Logger.Debug($"[auto_fill] normalized color: raw=[{rawTarget.R},{rawTarget.G},{rawTarget.B}] -> predefined=[{fillTarget.R},{fillTarget.G},{fillTarget.B}] diff={minDiff}");
        var fillTargets = new List<BgrColor> { fillTarget };
        Logger.Debug($"[auto_fill] fillTargets={fillTargets.Count} targets=[{string.Join(",", fillTargets.Select(c => $"[{c.R},{c.G},{c.B}]"))}]");
        var workers = ReadScanWorkers();
        _options.ScanWorkers = workers;
        if (textCores.Text != workers.ToString())
        {
            textCores.Text = workers.ToString();
        }
        Logger.Debug($"[auto_fill] workers={workers} step={_options.ScanStep}");
        _state.StartAutoFill();
        btnFill.Enabled = false;
        HidePreviewOverlay();
        Logger.Debug("[auto_fill] starting scan task...");
        Thread.Sleep(200);
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        Logger.Debug("[auto_fill] scanning...");
        Task.Run(() =>
        {
            try
            {
                var points = ScanMatchingPoints(snapshot.recordedRange.Value, fillTargets, token);
                Logger.Debug($"[auto_fill] scan returned points={points.Count} cancelled={token.IsCancellationRequested}");
                if (token.IsCancellationRequested)
                {
                    Logger.Debug("[scan] canceled before apply");
                    return;
                }
                _state.SetAutoFillPoints(points);
                Logger.Debug($"[auto_fill] enabled points={points.Count}");

                if (points.Count > 0)
                {
                    // 对点进行聚类排序，使覆盖层显示顺序与实际填色顺序一致
                    var clusters = FillPlanner.ClusterPoints(points, _options.ScanStep, _options.ClusterNeighborDistance);
                    var orderedPoints = FillPlanner.FlattenClusters(clusters);
                    Logger.Debug($"[auto_fill] clustered into {clusters.Count} groups, ordered={orderedPoints.Count} points");

                    // 初始化填充覆盖层，使用聚类后的顺序
                    Logger.Debug($"[auto_fill] setting up fill overlay with {orderedPoints.Count} points");
                    BeginInvoke((Action)(() => SetOverlayFillPoints(new List<Point>(orderedPoints), false)));
                    Logger.Debug($"[auto_fill] executing {orderedPoints.Count} fill points...");
                    ExecuteAutoFillPoints(orderedPoints, false, token);
                    Logger.Debug($"[auto_fill] ExecuteAutoFillPoints completed");
                }
                else
                {
                    Logger.Debug("[auto_fill] no points found, fill skipped");
                    BeginInvoke((Action)(() =>
                    {
                        MessageBox.Show("未找到匹配颜色的像素点，请检查颜色容差设置", "提示");
                    }));
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[scan] failed: {ex}");
            }
            finally
            {
                Logger.Debug("[auto_fill] task finally: restoring UI");
                BeginInvoke((Action)(() =>
                {
                    btnFill.Enabled = true;
                    _state.StopAll();
                    ClearOverlayFill();
                    RestorePreviewOverlayIfChecked();
                    var cts = Interlocked.Exchange(ref _scanCts, null);
                    cts?.Dispose();
                    Logger.Debug("[auto_fill] task cleanup done");
                }));
            }
        }, token);
    }

    private List<Point> ScanMatchingPoints(Rectangle rect, List<BgrColor> bgrs, CancellationToken token)
    {
        var points = new List<Point>();
        var whitePoints = new List<Point>();
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
                    BgrColor? matchedTarget = null;
                    foreach (var rbgr in bgrs)
                    {
                        int diff = bgr.MaxDiff(rbgr);
                        if (diff < localMin)
                        {
                            localMin = diff;
                            matchedTarget = rbgr;
                        }
                    }
                    minDiff = Math.Min(minDiff, localMin);
                    maxDiff = Math.Max(maxDiff, localMin);
                    sumDiff += localMin;
                    count++;
                    if (localMin <= _options.ColorTol)
                    {
                        var point = new Point(rect.Left + x, rect.Top + y);
                        if (matchedTarget.HasValue && IsWhite(matchedTarget.Value))
                        {
                            whitePoints.Add(point);
                        }
                        else
                        {
                            points.Add(point);
                        }
                    }
                    done++;
                    MaybeUpdateProgress(done);
                }
            }
            _state.SetScanProgress(total, done);
            whitePoints.AddRange(points);
            points = whitePoints;
        }
        else
        {
            var pointsBag = new ConcurrentBag<Point>();
            var whitePointsBag = new ConcurrentBag<Point>();
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
                            BgrColor? matchedTarget = null;
                            foreach (var rbgr in bgrs)
                            {
                                int diff = bgr.MaxDiff(rbgr);
                                if (diff < localMin)
                                {
                                    localMin = diff;
                                    matchedTarget = rbgr;
                                }
                            }
                            local.Min = Math.Min(local.Min, localMin);
                            local.Max = Math.Max(local.Max, localMin);
                            local.Sum += localMin;
                            local.Count++;
                            if (localMin <= _options.ColorTol)
                            {
                                var point = new Point(rect.Left + x, rect.Top + y);
                                if (matchedTarget.HasValue && IsWhite(matchedTarget.Value))
                                {
                                    whitePointsBag.Add(point);
                                }
                                else
                                {
                                    pointsBag.Add(point);
                                }
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
            points = new List<Point>(whitePointsBag);
            points.AddRange(pointsBag);
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
            if (checkShowRange.Checked)
            {
                RefreshRangePreview();
            }
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
            Logger.Debug("[cancel] CancelScan: cancelling and disposing scanCts");
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
        // 大量填涂时可能出现卡顿，导致单次空格被吞掉而触发无效。
        // 默认连续发送 2 次，并在两次之间留出间隔，保证至少有一次能被目标窗口收到。
        var repeat = _options.SpaceRepeatCount < 1 ? 1 : _options.SpaceRepeatCount;
        const ushort vk = 0x20;
        for (int i = 0; i < repeat; i++)
        {
            if (i > 0 && _options.SpaceRepeatGapMs > 0)
            {
                Thread.Sleep(_options.SpaceRepeatGapMs);
            }
            Logger.Debug($"[action] send space (#{i + 1}/{repeat})");
            SendKey(vk);
        }
    }

    /// <summary>
    /// 填色状态专用的空格发送：使用更短的延迟和更少的重复次数以加快连续填色速度。
    /// </summary>
    private void SendSpaceForFill()
    {
        var repeat = _options.FillSpaceRepeatCount < 1 ? 1 : _options.FillSpaceRepeatCount;
        const ushort vk = 0x20;
        for (int i = 0; i < repeat; i++)
        {
            if (i > 0 && _options.FillSpaceRepeatGapMs > 0)
            {
                Thread.Sleep(_options.FillSpaceRepeatGapMs);
            }
            Logger.Debug($"[action] send space fill (#{i + 1}/{repeat})");
            SendKeyWithDelay(vk, _options.FillActionDelayMs);
        }
    }

    private void SendKey(ushort vk)
    {
        SendKeyWithDelay(vk, _options.ActionDelayMs);
    }

    /// <summary>
    /// 发送按键，使用指定的延迟。
    /// 在调用 SendInput 之前会重新确认目标窗口的前台焦点，
    /// 如果 SendInput 返回 0 则自动重试（最多 2 次），每次重试前重新获取焦点。
    /// </summary>
    private void SendKeyWithDelay(ushort vk, int delayMs)
    {
        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }
        uint scan = NativeMethods.MapVirtualKey(vk, 0);
        if (_options.Debug)
        {
            Logger.Debug($"[action] send key vk=0x{vk:X2} scan=0x{scan:X2} delay={delayMs}");
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

        // 在 SendInput 前重新确认前台焦点，确保目标窗口处于前台
        EnsureForegroundForSendInput();

        // 必须在 SendInput 返回后立即保存 LastError，否则可能被后续代码覆盖
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        var lastErr = Marshal.GetLastWin32Error();
        if (sent == 0)
        {
            // SendInput 失败，尝试重试（最多 2 次）
            const int maxRetries = 2;
            for (int retry = 1; retry <= maxRetries; retry++)
            {
                Logger.Error($"[action] SendInput failed sent=0 lastErr={lastErr} (0x{lastErr:X8}), retry #{retry}/{maxRetries}");
                // 重新确认前台焦点并等待切换生效
                EnsureForegroundForSendInput();
                Thread.Sleep(50);
                sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
                lastErr = Marshal.GetLastWin32Error();
                if (sent != 0)
                {
                    Logger.Error($"[action] SendInput retry #{retry} succeeded sent={sent}");
                    break;
                }
            }
            if (sent == 0)
            {
                var fg = NativeMethods.GetForegroundWindow();
                Logger.Error($"[action] SendInput failed after {maxRetries} retries sent={sent} lastErr={lastErr} (0x{lastErr:X8}) fgHwnd={fg}");
                return;
            }
        }

        if (_options.Debug)
        {
            Logger.Debug($"[action] SendInput ok sent={sent}");
        }
    }

    /// <summary>
    /// 在 SendInput 前重新获取光标位置下的窗口并调用 SetForegroundWindow 确保前台焦点。
    /// 使用 AttachThreadInput 绑定当前线程与目标窗口线程的输入队列，
    /// 以确保 SetForegroundWindow 在后台线程上也能成功。
    /// </summary>
    private void EnsureForegroundForSendInput()
    {
        NativeMethods.GetCursorPos(out var pt);
        var hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // 使用 AttachThreadInput 辅助 SetForegroundWindow 在后台线程上成功
        uint currentTid = NativeMethods.GetCurrentThreadId();
        uint targetTid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        bool attached = false;
        if (targetTid != 0 && targetTid != currentTid)
        {
            attached = NativeMethods.AttachThreadInput(currentTid, targetTid, true);
            if (_options.Debug)
            {
                Logger.Debug($"[action] AttachThreadInput attach={attached} curTid={currentTid} tgtTid={targetTid}");
            }
        }

        var ok = NativeMethods.SetForegroundWindow(hwnd);
        if (_options.Debug)
        {
            var fg = NativeMethods.GetForegroundWindow();
            Logger.Debug($"[action] EnsureForeground hwnd={hwnd} setFgOk={ok} actualFg={fg}");
        }

        // 等待焦点切换生效（30ms 足够让 Windows 完成前台切换）
        Thread.Sleep(30);

        // 解绑线程输入队列
        if (attached)
        {
            NativeMethods.AttachThreadInput(currentTid, targetTid, false);
            if (_options.Debug)
            {
                Logger.Debug($"[action] AttachThreadInput detach curTid={currentTid} tgtTid={targetTid}");
            }
        }
    }

    private void SendMouseClick(uint downFlag, uint upFlag)
    {
        // 在 SendInput 前重新确认前台焦点
        EnsureForegroundForSendInput();

        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.INPUTUNION
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = 0,
                        dwFlags = downFlag,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            },
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.INPUTUNION
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = 0,
                        dwFlags = upFlag,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        };
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        var lastErr = Marshal.GetLastWin32Error();
        if (sent == 0)
        {
            // SendInput 失败，尝试重试（最多 2 次）
            const int maxRetries = 2;
            for (int retry = 1; retry <= maxRetries; retry++)
            {
                Logger.Error($"[action] SendMouseClick failed sent=0 lastErr={lastErr} (0x{lastErr:X8}), retry #{retry}/{maxRetries}");
                EnsureForegroundForSendInput();
                Thread.Sleep(50);
                sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
                lastErr = Marshal.GetLastWin32Error();
                if (sent != 0)
                {
                    Logger.Error($"[action] SendMouseClick retry #{retry} succeeded sent={sent}");
                    break;
                }
            }
            if (sent == 0)
            {
                var fg = NativeMethods.GetForegroundWindow();
                Logger.Error($"[action] SendMouseClick failed after {maxRetries} retries sent={sent} lastErr={lastErr} (0x{lastErr:X8}) fgHwnd={fg}");
            }
        }
    }

    private void ClickCurrentPosition()
    {
        Thread.Sleep(_options.ActionDelayMs);
        Logger.Debug("[action] click left (down+up) via SendInput");
        SendMouseClick(NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP);
    }

    private void ClickRightCurrentPosition()
    {
        Thread.Sleep(_options.ActionDelayMs);
        Logger.Debug("[action] click right (down+up) via SendInput");
        SendMouseClick(NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP);
    }

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
        var moduleName = curModule?.ModuleName;
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            Logger.Error("[hook] failed to install keyboard hook: current module name is unavailable");
            return;
        }

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(moduleName),
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
        _startupActivationTimer?.Stop();
        _startupActivationTimer?.Dispose();
        _startupActivationTimer = null;
        CancelScan();
        CancelAutoAll();
        Unhook();
        _previewOverlay?.Close();
        _previewOverlay?.Dispose();
        _previewOverlay = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == NativeMethods.VK_ESCAPE)
            {
                Logger.Debug($"[hook] ESC pressed, stopping all");
                BeginInvoke((Action)(() =>
                {
                    Logger.Debug("[hook] ESC BeginInvoke: calling StopAll/CancelScan/CancelAutoAll");
                    _state.StopAll();
                    CancelScan();
                    CancelAutoAll();
                    Logger.Debug("[hook] ESC: all stopped, restoring overlay if needed");
                    RestorePreviewOverlayIfChecked();
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
                Logger.Debug($"[hook] A pressed, recording color");
                BeginInvoke((Action)(() =>
                {
                    // 光标位于本程序窗口上时按 A：取色无意义，且此刻本程序窗口往往是前台，
                    // 后续发送的 I 键 / 左键会被本程序吞掉，或左键直接点到自己的按钮上
                    // （btnFill/btnAutoFillAll），表现为“按 A 却触发空格/涂色”。直接中止这次取色。
                    var pos = Cursor.Position;
                    if (IsCursorOverSelf(pos))
                    {
                        Logger.Debug($"[hook] A ignored: cursor over self at ({pos.X},{pos.Y})");
                        return;
                    }
                    PerformColorRecordAndAction();
                }));
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private (BgrColor hover, BgrColor clear) PerformColorRecordAndAction()
    {
        Logger.Debug("[record] PerformColorRecordAndAction start");
        // 取色前隐藏覆盖层，避免读到覆盖层的颜色
        HidePreviewOverlay();
        try
        {
            var pos = Cursor.Position;
            BgrColor hover;
            BgrColor clear;
            using (var dc = new ScreenDc())
            {
                hover = dc.GetPixel(pos.X, pos.Y);
            }
            Logger.Debug($"[record] hover at ({pos.X},{pos.Y}) rgb=[{hover.R},{hover.G},{hover.B}]");
            var safe = PickSafePos(_state.Snapshot().recordedRange);
            if (safe.HasValue)
            {
                Logger.Debug($"[record] moving cursor to safe pos ({safe.Value.X},{safe.Value.Y})");
                NativeMethods.SetCursorPos(safe.Value.X, safe.Value.Y);
                Thread.Sleep(30);
            }
            using (var dc = new ScreenDc())
            {
                clear = dc.GetPixel(pos.X, pos.Y);
            }
            NativeMethods.SetCursorPos(pos.X, pos.Y);
            Logger.Debug($"[record] clear at ({pos.X},{pos.Y}) rgb=[{clear.R},{clear.G},{clear.B}]");

            _state.RecordColors(new List<BgrColor> { hover, clear }, new List<BgrColor> { hover, clear }, pos);
            Logger.Debug($"[record] raw_colors_rgb=[{string.Join(",", hover.ToRgbArray())}],[{string.Join(",", clear.ToRgbArray())}] pos=({pos.X},{pos.Y})");
            FocusTargetUnderCursor();
            Logger.Debug("[action] send key I");
            Thread.Sleep(50);
            SendKey(NativeMethods.VK_I);
            Thread.Sleep(50);
            Logger.Debug("[action] click left");
            ClickCurrentPosition();
            Logger.Debug("[record] PerformColorRecordAndAction done");
            return (hover, clear);
        }
        finally
        {
            // 恢复显示由调用方决定，这里不主动恢复
        }
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
            // WindowFromPoint 可能返回子窗口句柄，而 SetForegroundWindow 需要顶层窗口；
            // 解析到根窗口，保证后续焦点设置与确认比较都针对同一个顶层句柄。
            var rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (rootHwnd == IntPtr.Zero)
            {
                rootHwnd = hwnd;
            }

            // 使用 AttachThreadInput 辅助 SetForegroundWindow，确保在后台线程上也能成功
            uint currentTid = NativeMethods.GetCurrentThreadId();
            uint targetTid = NativeMethods.GetWindowThreadProcessId(rootHwnd, out var pid);
            bool attached = false;
            if (targetTid != 0 && targetTid != currentTid)
            {
                attached = NativeMethods.AttachThreadInput(currentTid, targetTid, true);
                if (_options.Debug)
                {
                    Logger.Debug($"[action] FocusTarget AttachThreadInput attach={attached} curTid={currentTid} tgtTid={targetTid}");
                }
            }

            var ok = NativeMethods.SetForegroundWindow(rootHwnd);
            if (_options.Debug)
            {
                Logger.Debug($"[action] focus hwnd={rootHwnd} tid={targetTid} pid={pid} ok={ok} attach={attached}");
            }

            // 解绑线程输入队列
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentTid, targetTid, false);
                if (_options.Debug)
                {
                    Logger.Debug($"[action] FocusTarget AttachThreadInput detach curTid={currentTid} tgtTid={targetTid}");
                }
            }

            // SetForegroundWindow 是异步的：旧前台窗口可能尚未真正切走，此时紧接着发送的 I 键
            // 会被旧前台窗口（往往是本程序自身）吞掉，导致取色失败、随后的左键落到游戏上被当成一次涂色。
            // 这里轮询确认前台已切换到目标窗口后再继续，固定 sleep 无法保证切换完成。
            if (!WaitForForeground(rootHwnd, 150))
            {
                Logger.Debug($"[action] FocusTarget foreground not confirmed hwnd={rootHwnd}");
            }
        }
        Thread.Sleep(_options.ActionDelayMs);
    }

    /// <summary>
    /// 轮询确认目标窗口已成为前台窗口，最多等待 timeoutMs 毫秒。
    /// SetForegroundWindow 是异步的，固定 sleep 无法保证切换完成。
    /// </summary>
    private static bool WaitForForeground(IntPtr hwnd, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (NativeMethods.GetForegroundWindow() == hwnd)
            {
                return true;
            }
            Thread.Sleep(10);
        }
        return NativeMethods.GetForegroundWindow() == hwnd;
    }

    private void CancelAutoAll()
    {
        _autoAllProgressActive = false;
        var cts = Interlocked.Exchange(ref _autoAllCts, null);
        if (cts != null)
        {
            Logger.Debug("[cancel] CancelAutoAll: cancelling and disposing autoAllCts");
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void BeginAutoFillAll()
    {
        Logger.Debug("[ui] BeginAutoFillAll clicked");
        if (!btnAutoFillAll.Enabled)
        {
            Logger.Debug("[auto_all] button disabled, skipping");
            return;
        }

        _options.ScanStep = ReadScanStep();
        if (ScanStep.Text != _options.ScanStep.ToString())
        {
            ScanStep.Text = _options.ScanStep.ToString();
        }

        var snapshot = _state.Snapshot();
        if (!snapshot.recordedRange.HasValue)
        {
            Logger.Debug("[auto_all] no range, showing message box");
            MessageBox.Show("请框选范围", "提示");
            return;
        }

        var htmlColors = GetPredefinedColors();
        if (htmlColors.Count == 0)
        {
             Logger.Debug("[auto_all] no predefined colors found");
             MessageBox.Show("未找到颜色定义", "错误");
             return;
        }
        Logger.Debug($"[auto_all] found {htmlColors.Count} colors, range={snapshot.recordedRange.Value}");

        var workers = ReadScanWorkers();
        _options.ScanWorkers = workers;
        if (textCores.Text != workers.ToString())
        {
            textCores.Text = workers.ToString();
        }

        btnAutoFillAll.Enabled = false;
        btnFill.Enabled = false;
        btnRange.Enabled = false;
        HidePreviewOverlay();

        CancelScan();
        CancelAutoAll();
        _autoAllCts = new CancellationTokenSource();
        var token = _autoAllCts.Token;
        _autoAllProgressCurrent = 0;
        _autoAllProgressTotal = 0;
        _autoAllProgressStart = DateTime.Now;
        _autoAllProgressActive = true;
        Logger.Debug($"[auto_all] starting scan for all colors...");

        Task.Run(() =>
        {
            try
            {
                Logger.Debug("[auto_all] scanning...");
                var groups = ScanMatchingPointsForAllColors(snapshot.recordedRange.Value, htmlColors, token);
                Logger.Debug($"[auto_all] scan returned groups={groups.Count} cancelled={token.IsCancellationRequested}");
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

                Logger.Debug($"[auto_all] executing fill for {totalPoints} points...");
                ExecuteAutoFillAll(groups, htmlColors, token);
                Logger.Debug("[auto_all] ExecuteAutoFillAll completed");
            }
            catch (Exception ex)
            {
                Logger.Error($"[auto_all] error: {ex}");
            }
            finally
            {
                Logger.Debug("[auto_all] task finally: restoring UI");
                BeginInvoke((Action)(() =>
                {
                    _autoAllProgressActive = false;
                    btnAutoFillAll.Enabled = true;
                    btnFill.Enabled = true;
                    btnRange.Enabled = true;
                    RestorePreviewOverlayIfChecked();
                    var cts = Interlocked.Exchange(ref _autoAllCts, null);
                    cts?.Dispose();
                    Logger.Debug("[auto_all] task cleanup done");
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
        Logger.Debug($"[scan_all] ScanMatchingPointsForAllColors rect={rect} targets={targets.Count}");
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

                int localMin = int.MaxValue;
                BgrColor? bestTarget = null;
                foreach (var target in targets)
                {
                    int diff = pixelColor.MaxDiff(target);
                    if (diff < localMin)
                    {
                        localMin = diff;
                        bestTarget = target;
                    }
                }

                if (bestTarget.HasValue && localMin <= _options.ColorTol)
                {
                    groups[bestTarget.Value].Add(new Point(rect.Left + x, rect.Top + y));
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
        int totalPoints = result.Values.Sum(l => l.Count);
        Logger.Debug($"[scan_all] result: {result.Count} colors matched, {totalPoints} total points");
        return result;
    }

    private void ExecuteAutoFillAll(Dictionary<BgrColor, List<Point>> groups, List<BgrColor> order, CancellationToken token)
    {
        Logger.Debug($"[exec_all] ExecuteAutoFillAll start: groups={groups.Count} order={order.Count}");
        _autoFillAllStartTime = DateTime.Now;
        int total = groups.Values.Sum(l => l.Count);
        int processed = 0;
        var orderedColors = order
            .Where(IsWhite)
            .Concat(order.Where(color => !IsWhite(color)))
            .ToList();
        bool whiteColorCompleted = false;

        // 构建覆盖层点列表：按聚类后的实际处理顺序排列，使覆盖层显示与填色进度一致
        var allOrderedPoints = new List<Point>();
        foreach (var color in orderedColors)
        {
            if (!groups.TryGetValue(color, out var colorPoints) || colorPoints.Count == 0)
            {
                continue;
            }
            var clusters = FillPlanner.ClusterPoints(colorPoints, _options.ScanStep, _options.ClusterNeighborDistance);
            allOrderedPoints.AddRange(FillPlanner.FlattenClusters(clusters));
        }
        Logger.Debug($"[exec_all] allOrderedPoints={allOrderedPoints.Count} (clustered order), whites first then colors");
        BeginInvoke((Action)(() => SetOverlayFillPoints(allOrderedPoints, true)));

        // Ensure we yield focus away from our form initially
        Thread.Sleep(500);

        foreach (var color in orderedColors)
        {
            if (token.IsCancellationRequested)
            {
                Logger.Debug("[exec_all] cancelled during color loop");
                return;
            }
            if (!groups.ContainsKey(color))
            {
                Logger.Debug($"[exec_all] color [{color.R},{color.G},{color.B}] not in groups, skipping");
                continue;
            }

            var points = groups[color];
            if (points.Count == 0)
            {
                Logger.Debug($"[exec_all] color [{color.R},{color.G},{color.B}] has 0 points, skipping");
                continue;
            }
            bool currentColorIsWhite = IsWhite(color);
            Logger.Debug($"[exec_all] processing color [{color.R},{color.G},{color.B}] isWhite={currentColorIsWhite} points={points.Count}");

            // 对当前颜色的所有点进行聚类+最近邻排序，使同色相邻点可以快速划过
            var clusters = FillPlanner.ClusterPoints(points, _options.ScanStep, _options.ClusterNeighborDistance);
            var orderedPoints = FillPlanner.FlattenClusters(clusters);
            Logger.Debug($"[exec_all] color clustered into {clusters.Count} groups, ordered={orderedPoints.Count}");

            int startIndex = 0;
            bool firstPointHandled = false;
            while (startIndex < orderedPoints.Count)
            {
                if (token.IsCancellationRequested) return;

                var first = orderedPoints[startIndex];
                Logger.Debug($"[exec_all] color first pt ({startIndex}/{orderedPoints.Count}) pos=({first.X},{first.Y})");
                Cursor.Position = first;
                Thread.Sleep(_options.ActionDelayMs);

                // Focus on the window under cursor FIRST and wait longer
                ClickRightCurrentPosition();
                Thread.Sleep(200); // Increased delay to ensure focus is applied

                var recorded = PerformColorRecordAndAction();
                if (token.IsCancellationRequested) return;

                // 颜色检测完成后重新显示剩余像素点
                // 低频更新：仅每 10 个点或首个点时更新覆盖层
                if (processed % 10 == 0 || processed == 1)
                {
                    BeginInvoke((Action)(() => SetOverlayFillStartIndex(processed)));
                }

                if (whiteColorCompleted && !currentColorIsWhite && IsWhite(recorded.clear))
                {
                    Logger.Debug($"[auto_all] skip white sample while switching color target={FormatBgr(color)} pt=({first.X},{first.Y}) clear={FormatBgr(recorded.clear)}");
                    processed++;
                    UpdateAutoAllOverlay(processed);
                    UpdateAutoAllProgress(processed, total);
                    startIndex++;
                    continue;
                }

                Thread.Sleep(_options.ColorPickToFillDelayMs);
                SendSpace();
                processed++;
                UpdateAutoAllOverlay(processed);
                UpdateAutoAllProgress(processed, total);
                startIndex++;
                firstPointHandled = true;
                break;
            }

            if (!firstPointHandled)
            {
                Logger.Debug($"[exec_all] color [{color.R},{color.G},{color.B}] no first point handled, skipping remaining");
                if (currentColorIsWhite)
                {
                    whiteColorCompleted = true;
                }
                continue;
            }

            // Handle subsequent points for this color — 使用快速填色+聚类顺序
            for (int i = startIndex; i < orderedPoints.Count; i++)
            {
                if (token.IsCancellationRequested) return;
                Cursor.Position = orderedPoints[i];
                // FocusTargetUnderCursor();
                SendSpaceForFill();
                processed++;
                UpdateAutoAllOverlay(processed);
                UpdateAutoAllProgress(processed, total);

                // 簇内连续点之间的可选等待（快速划过时通常为0）
                if (i < orderedPoints.Count - 1 && _options.ClusterFillStepDelayMs > 0)
                {
                    Thread.Sleep(_options.ClusterFillStepDelayMs);
                }
            }

            if (currentColorIsWhite)
            {
                whiteColorCompleted = true;
                Logger.Debug("[exec_all] white color completed, whiteColorCompleted=true");
            }
        }
        Logger.Debug($"[exec_all] ExecuteAutoFillAll done: processed={processed}/{total}");
    }

    private void UpdateAutoAllOverlay(int processed)
    {
        // 低频更新覆盖层进度：仅每 10 个点时更新，减少 BeginInvoke 对焦点的干扰
        // 最后一个点的更新由 ExecuteAutoFillAll 结束时的 finally 块中 RestorePreviewOverlayIfChecked 处理
        if (processed % 10 == 0)
        {
            BeginInvoke((Action)(() => SetOverlayFillStartIndex(processed)));
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

    private enum SpeedPreset
    {
        Balanced,
        Extreme
    }

    /// <summary>
    /// 应用速度预设。
    /// 平衡：总耗时控制在 100ms 以内，保证准确性。
    /// 极致速度：削减准确性，越快越好。
    /// </summary>
    private void ApplySpeedPreset(SpeedPreset preset)
    {
        switch (preset)
        {
            case SpeedPreset.Balanced:
                // 平衡：40ms 延迟 + 2 次空格 + 10ms 间隔 ≈ 90ms/点（<100ms，保证准确性）
                _options.FillActionDelayMs = 40;
                _options.FillSpaceRepeatCount = 2;
                _options.FillSpaceRepeatGapMs = 10;
                Logger.Debug("[speed] preset=Balanced (delay=40ms repeat=2 gap=10ms ~90ms/pt)");
                break;
            case SpeedPreset.Extreme:
                // 极致速度：20ms 延迟 + 1 次空格 + 0ms 间隔 ≈ 20ms/点
                _options.FillActionDelayMs = 20;
                _options.FillSpaceRepeatCount = 1;
                _options.FillSpaceRepeatGapMs = 0;
                Logger.Debug("[speed] preset=Extreme (delay=20ms repeat=1 gap=0ms ~20ms/pt)");
                break;
        }
    }

    private void ApplyLayout(UiLayoutMode mode)
    {
        SuspendLayout();
        try
        {
            if (mode == UiLayoutMode.Vertical)
            {
                ClientSize = new Size(383, 720);
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
                checkShowRange.Visible = true;
                panelLeft.Location = new Point(12, 62);
                panelLeft.Size = new Size(108, 50);
                panelRight.Location = new Point(133, 62);
                panelRight.Size = new Size(106, 50);
                labelCores.Text = "调用CPU数量";
                btnAutoCores.Text = "自动决定CPU数量";
                label7.Text = "扫描步长";
                btnRange.Text = "划取检测范围";
                btnFill.Text = "自动检测及填充";
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
                checkShowRange.Location = new Point(12, 335);
                btnFill.Location = new Point(12, 371);
                btnFill.Size = new Size(122, 26);
                label10.Location = new Point(167, 371);
                labelScan.Location = new Point(14, 412);
                labelScanValue.Location = new Point(160, 412);
                progressScan.Location = new Point(14, 435);
                progressScan.Size = new Size(351, 28);
                labelMatchProgress.Location = new Point(17, 486);
                labelMatchValue.Location = new Point(160, 486);
                progressMatch.Location = new Point(12, 509);
                progressMatch.Size = new Size(351, 28);
                btnAutoFillAll.Location = new Point(12, 550);
                btnAutoFillAll.Size = new Size(160, 26);
                radioSpeedBalanced.Text = "平衡";
                radioSpeedBalanced.Location = new Point(178, 553);
                radioSpeedExtreme.Text = "极致速度";
                radioSpeedExtreme.Location = new Point(252, 553);
                radioSpeedBalanced.Visible = true;
                radioSpeedExtreme.Visible = true;
                btnToggleLayout.Location = new Point(218, 667);
                btnToggleLayout.Size = new Size(145, 26);
                labelAutoAll.Location = new Point(17, 590);
                labelAutoAllValue.Location = new Point(160, 590);
                progressAutoAll.Location = new Point(12, 615);
                progressAutoAll.Size = new Size(351, 28);
                linkGithubOrUpdate.Location = new Point(12, 671);
                labelCurrentVersion.Location = new Point(12, 649);
                btnToggleLayout.Text = "切换为精简布局";
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
                checkShowRange.Location = new Point(248, 128);
                checkShowRange.Visible = true;

                // 区域2：动作按钮 + 进度
                btnFill.Text = "自动";
                btnFill.Location = new Point(430, 20);
                btnFill.Size = new Size(58, 26);

                btnAutoFillAll.Text = "全自动";
                btnAutoFillAll.Location = new Point(494, 20);
                btnAutoFillAll.Size = new Size(76, 26);
                radioSpeedBalanced.Text = "平衡";
                radioSpeedBalanced.Location = new Point(430, 108);
                radioSpeedExtreme.Text = "极致";
                radioSpeedExtreme.Location = new Point(492, 108);
                radioSpeedBalanced.Visible = true;
                radioSpeedExtreme.Visible = true;

                labelAutoAll.Text = "总进度";
                labelAutoAll.Location = new Point(430, 58);
                labelAutoAllValue.Location = new Point(500, 58);
                progressAutoAll.Location = new Point(430, 82);
                progressAutoAll.Size = new Size(250, 22);

                // 区域1底部：入口
                labelCurrentVersion.Location = new Point(10, 124);
                linkGithubOrUpdate.Location = new Point(10, 146);
                btnToggleLayout.Location = new Point(548, 142);
                btnToggleLayout.Size = new Size(145, 26);
                btnToggleLayout.Text = "切换为完整布局";

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
        // 更新检测策略（全程静默，不弹任何提示）：
        // 1) 优先走 GitHub API（原方案）；
        // 2) API 走不通（如共享 IP 命中 60 次/小时限额返回 403）时，退回 github.com 的 302 重定向，
        //    从 Location 头解析最新版本号（不受 API 限额限制）；
        // 3) 两者都拿不到结果时，记录一条 error 日志后返回。
        string apiError = string.Empty;
        string redirectError = string.Empty;
        string? latestTag = null;

        try
        {
            latestTag = await TryGetLatestTagViaApiAsync();
        }
        catch (Exception ex)
        {
            apiError = ex.Message;
        }

        if (string.IsNullOrEmpty(latestTag))
        {
            try
            {
                latestTag = await TryGetLatestTagViaRedirectAsync();
            }
            catch (Exception ex)
            {
                redirectError = ex.Message;
            }
        }

        if (string.IsNullOrEmpty(latestTag))
        {
            Logger.Error($"[update] check failed: api=({apiError}); redirect=({redirectError})");
            return;
        }

        if (!TryParseVersion(latestTag, out var latestVersion) || latestVersion <= _currentAppVersion)
        {
            return;
        }

        _updateTargetUrl = $"{RepoUrl}/releases/tag/{latestTag}";
        linkGithubOrUpdate.Text = $"发现新版本 {latestTag}，点击下载";
        linkGithubOrUpdate.LinkColor = Color.OrangeRed;
    }

    // 通过 GitHub API 获取最新 release 的 tag。失败时抛异常，由调用方决定是否回退。
    private async Task<string> TryGetLatestTagViaApiAsync()
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("wplace_canYouHelpMe-update-check");
        using var response = await http.GetAsync(LatestReleaseApiUrl);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"api status {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        if (!json.RootElement.TryGetProperty("tag_name", out var tagElem))
        {
            throw new InvalidOperationException("api response missing tag_name");
        }

        var tag = tagElem.GetString();
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new InvalidOperationException("api tag_name empty");
        }

        return tag;
    }

    // 通过 github.com 的 302 重定向获取最新 release 的 tag。
    // /releases/latest 会 302 跳转到 /releases/tag/{tag}（仅指向最新非预发布版本），
    // 走网页层而非 api.github.com，不受未认证 IP 的 60 次/小时限额限制。
    private async Task<string> TryGetLatestTagViaRedirectAsync()
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(6)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("wplace_canYouHelpMe-update-check");
        using var response = await http.GetAsync(LatestReleaseRedirectUrl);

        var location = response.Headers.Location?.OriginalString;
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new InvalidOperationException($"redirect missing Location (status {(int)response.StatusCode} {response.ReasonPhrase})");
        }

        // 形如 https://github.com/.../releases/tag/1.2.0，取最后一段作为 tag。
        var tag = location.TrimEnd('/').Split('/').Last();
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new InvalidOperationException("redirect Location has no tag segment");
        }

        return tag;
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

    private static Version GetCurrentAppVersion()
    {
        if (TryParseVersion(Application.ProductVersion, out var parsed))
        {
            return parsed;
        }

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v ?? new Version(0, 0, 0, 0);
    }

    private static string GetCurrentVersionText(Version version)
    {
        int build = version.Build >= 0 ? version.Build : 0;
        return $"{version.Major}.{version.Minor}.{build}";
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



