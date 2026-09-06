using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.IO;
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

    private const string RepoUrl = ReleaseUpdateChecker.RepositoryUrl;
    private const int DefaultScanWorkers = 1;
    private const int DefaultScanStep = 10;
    private const int LayoutDesignDpi = 96;
    private const int CompactLayoutWidth = 740;
    private const int CompactLayoutHeight = 192;
    private const int ClearSampleMinWaitMs = 64;
    private const int ClearSampleTimeoutMs = 240;
    private const int ClearSamplePollMs = 16;
    private const int ClearSampleStableReads = 3;
    private const int ClearSampleStableTolerance = 1;
    private const int ClearSamplePaletteTolerance = 12;

    private readonly Options _options;
    private readonly uint _showMainWindowMessage;
    private UserSettings _settings = new();
    private readonly RuntimeState _state = new();
    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    // 是否正在划取检测范围（SelectionForm 模态打开中）。为 true 时，ESC 不被全局钩子吞掉，
    // 放行给 SelectionForm 处理，使其能立即退出。
    private bool _selectingRange;
    private CancellationTokenSource? _autoAllCts;
    private CancellationTokenSource? _islandCts;
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
    private readonly Button btnRunIslandDetect = new();
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
        _colorRules = new ColorRuleSet(GetPredefinedColors());
        InitializeColorManagementUi();
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

        // 遗漏检测控件（仅手动按钮；自动填涂后不再自动触发）
        btnRunIslandDetect.Text = "运行遗漏检测";
        btnRunIslandDetect.UseVisualStyleBackColor = true;
        btnRunIslandDetect.Click += (_, _) => BeginIslandDetection();
        Controls.Add(btnRunIslandDetect);

        // 加载持久化的用户设置并应用到 UI（需在控件创建之后）
        LoadUserSettings();

        ApplyLayout(_layoutMode);

        // The handle (and therefore the monitor DPI) is known by Load time.
        // Reapply once before the form is first painted in case construction
        // happened before WinForms could resolve the target monitor DPI.
        Load += (_, _) => ApplyLayout(_layoutMode);

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

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyLayout(_layoutMode);
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

        bool operationRunning = _autoAllCts != null || _islandCts != null;
        bool fillPhase = _autoAllProgressTotal > 0;
        int taskTotal;
        int taskCurrent;
        DateTime taskStart;
        if (fillPhase)
        {
            taskTotal = _autoAllProgressTotal;
            taskCurrent = _autoAllProgressCurrent;
            taskStart = _autoAllProgressStart;
            labelAutoAll.Text = operationRunning ? "填色进度" : "任务进度";
        }
        else if (operationRunning || snapshot.scanTotal > 0)
        {
            taskTotal = snapshot.scanTotal;
            taskCurrent = snapshot.scanDone;
            taskStart = snapshot.scanStartTime;
            labelAutoAll.Text = operationRunning ? "扫描进度" : "任务进度";
        }
        else
        {
            taskTotal = 0;
            taskCurrent = 0;
            taskStart = DateTime.Now;
            labelAutoAll.Text = "任务进度";
        }

        progressAutoAll.Maximum = Math.Max(1, taskTotal);
        progressAutoAll.Value = Math.Min(Math.Max(0, taskCurrent), progressAutoAll.Maximum);
        var taskEta = operationRunning ? GetEta(taskStart, taskCurrent, taskTotal) : "";
        labelAutoAllValue.Text = $"{taskCurrent} / {taskTotal}{taskEta}";
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

    /// <summary>
    /// 判断取色样本是否命中本次自动化运行中已经取过的颜色（容差 = ColorTol）。
    /// 用于多色全自动填涂时跳过“取到旧笔色”的异常点，避免用旧笔色覆盖当前色组。
    /// </summary>
    private bool IsColorAlreadyPicked(BgrColor sample, HashSet<BgrColor> pickedColors)
    {
        if (pickedColors.Count == 0)
        {
            return false;
        }
        int tol = _options.ColorTol;
        foreach (var c in pickedColors)
        {
            if (sample.MaxDiff(c) <= tol)
            {
                return true;
            }
        }
        return false;
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

    private bool IsOperationActive()
    {
        return _autoAllCts != null || _islandCts != null;
    }

    private void SetOperationControlsEnabled(bool enabled)
    {
        btnRange.Enabled = enabled;
        btnAutoFillAll.Enabled = enabled;
        btnRunIslandDetect.Enabled = enabled;
        _colorManagerButton.Enabled = enabled;
        btnAutoCores.Enabled = enabled;
        btnToggleLayout.Enabled = enabled;
        textCores.Enabled = enabled;
        ScanStep.Enabled = enabled;
        checkShowRange.Enabled = enabled;
        radioSpeedBalanced.Enabled = enabled;
        radioSpeedExtreme.Enabled = enabled;
        linkGithubOrUpdate.Enabled = enabled;
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
        _selectingRange = true;
        try
        {
            sel.ShowDialog(this);
        }
        finally
        {
            _selectingRange = false;
        }
        Logger.Debug($"[range] SelectionForm closed selected={sel.SelectedRect.HasValue}");
        if (sel.SelectedRect.HasValue)
        {
            _state.SetRange(sel.SelectedRect.Value, sel.SelectedPolygon);
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
        var poly = _state.GetPolygon();
        var points = ScanPattern.GetGridPoints(rect, _options.ScanStep, poly);
        _state.SetPreviewPoints(points);
        if (_previewOverlay == null || _previewOverlay.IsDisposed)
        {
            Logger.Debug("[overlay] RefreshRangePreview: creating new overlay form");
            _previewOverlay = new PreviewOverlayForm();
        }
        _overlayFillMode = false;
        _overlayFillPoints = points;
        _overlayFillStartIndex = 0;
        _previewOverlay.SetData(rect, points, 0, poly);
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

        // 全自动或遗漏检测扫描期间隐藏覆盖层，避免把覆盖层内容扫进截图。
        // 建立待填点列表后由 SetOverlayFillPoints 切换到填充覆盖层。
        if ((_autoAllCts != null || _islandCts != null) && !_overlayFillMode)
        {
            if (_previewOverlay != null && _previewOverlay.Visible)
            {
                Logger.Debug("[overlay] UpdateOverlayFromState: scan active, hiding");
            }
            _previewOverlay?.Hide();
            return;
        }

        var range = snapshot.recordedRange.Value;
        var poly = _state.GetPolygon();

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
                EnsureOverlay().SetData(range, snapshot.previewPoints, 0, poly);
            }
            else if (_previewOverlay == null || _previewOverlay.IsDisposed)
            {
                Logger.Debug("[overlay] UpdateOverlayFromState: creating overlay for preview mode");
                EnsureOverlay().SetData(range, snapshot.previewPoints, 0, poly);
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
        EnsureOverlay().SetData(snapshot.recordedRange.Value, _overlayFillPoints, 0, _state.GetPolygon());
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

    private List<Point> ScanMatchingPoints(Rectangle rect, List<BgrColor> bgrs, CancellationToken token, List<Point>? polygon = null)
    {
        var poly = polygon != null && polygon.Count >= 3 ? new OrthogonalPolygon(polygon) : null;
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
                    if (poly != null && !poly.Contains(rect.Left + x, rect.Top + y))
                    {
                        done++;
                        continue;
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
                            if (poly != null && !poly.Contains(rect.Left + x, rect.Top + y))
                            {
                                continue;
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

    /// <summary>
    /// 无副作用的整数解析：仅当文本为合法正整数时返回该值，否则返回 fallback。
    /// 用于保存设置时读取控件值，避免触发 ReadScanWorkers/ReadScanStep 的弹窗与文本回写副作用。
    /// </summary>
    private static int SafeParseInt(string? text, int fallback)
    {
        return int.TryParse(text, out var value) && value > 0 ? value : fallback;
    }

    /// <summary>
    /// 加载持久化的用户设置并应用到 UI 与 _options。需在控件创建之后调用。
    /// </summary>
    private void LoadUserSettings()
    {
        _settings = UserSettings.Load();
        _options.ScanStep = _settings.ScanStep;
        // ScanWorkers 按 CPU 核数钳制，与 ReadScanWorkers 的上限一致，避免换机器后首次填充报“输入无效”
        int maxWorkers = Math.Max(1, Environment.ProcessorCount - 1);
        _options.ScanWorkers = Math.Max(1, Math.Min(_settings.ScanWorkers, maxWorkers));
        ScanStep.Text = _settings.ScanStep.ToString();
        textCores.Text = _options.ScanWorkers.ToString();

        // 启动时尚无 recordedRange，ToggleRangePreview->RefreshRangePreview 会安全早退
        checkShowRange.Checked = _settings.ShowRange;

        // 速度模式：设为当前已选值不触发 CheckedChanged，切换到另一项才会触发并应用对应预设
        if (_settings.SpeedPreset == UserSettings.SpeedExtreme)
        {
            radioSpeedExtreme.Checked = true;
        }
        else
        {
            radioSpeedBalanced.Checked = true;
        }
        Logger.Debug($"[settings] loaded step={_options.ScanStep} workers(applied)={_options.ScanWorkers} showRange={_settings.ShowRange} speed={_settings.SpeedPreset} skipIslandRec={_settings.SkipIslandRecommendation}");
    }

    /// <summary>
    /// 将当前 UI 状态写入 _settings 并持久化。在窗体关闭时调用。
    /// </summary>
    private void SaveUserSettings()
    {
        if (_settings == null)
        {
            return;
        }
        _settings.ScanWorkers = SafeParseInt(textCores.Text, _options.ScanWorkers);
        _settings.ScanStep = SafeParseInt(ScanStep.Text, _options.ScanStep);
        _settings.ShowRange = checkShowRange.Checked;
        _settings.SpeedPreset = radioSpeedExtreme.Checked ? UserSettings.SpeedExtreme : UserSettings.SpeedBalanced;
        // SkipIslandRecommendation 已在用户勾选“不再提示”时写入
        _settings.Save();
        Logger.Debug($"[settings] saved step={_settings.ScanStep} workers={_settings.ScanWorkers} showRange={_settings.ShowRange} speed={_settings.SpeedPreset} skipIslandRec={_settings.SkipIslandRecommendation}");
    }

    private void ShowInvalidInputMessage()
    {
        IslandConfirmDialog.Show(this, "输入内容无效。", "提示", false);
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

    private static Point? PickSafePos(Rectangle? avoidRect, Point? avoidPoint = null)
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
                bool outsideRange = avoidRect == null || !avoidRect.Value.Contains(pt);
                bool awayFromSample = !avoidPoint.HasValue ||
                    Math.Abs(pt.X - avoidPoint.Value.X) >= 50 ||
                    Math.Abs(pt.Y - avoidPoint.Value.Y) >= 50;
                if (outsideRange && awayFromSample)
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
        // 关闭时持久化用户设置（吞掉异常，绝不影响关闭流程）
        SaveUserSettings();
        _startupActivationTimer?.Stop();
        _startupActivationTimer?.Dispose();
        _startupActivationTimer = null;
        _colorToolTip.Dispose();
        CancelAutoAll();
        CancelIsland();
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
            // KBDLLHOOKSTRUCT.flags 位于偏移 8。LL 低级键盘钩子也会收到 SendInput 注入的事件，
            // 据此把“程序自己发出的按键”（injected）与“用户真实按键”区分开。
            int llFlags = Marshal.ReadInt32(lParam, 8);
            bool injected = (llFlags & (NativeMethods.LLKHF_INJECTED | NativeMethods.LLKHF_LOWER_IL_INJECTED)) != 0;
            // 注入事件（本程序 SendInput 发出的 I/空格）一律放行，否则会被下面的屏蔽逻辑吞掉，
            // 表现为“取不到色、只有首尾两个点被涂”。
            bool operationActive = !injected && IsOperationActive();

            if (vkCode == NativeMethods.VK_ESCAPE && !injected)
            {
                // 正在划取检测范围（SelectionForm 模态打开）时放行 ESC，
                // 让 SelectionForm.OnKeyDown 处理以立即退出；不在此吞掉。
                if (_selectingRange)
                {
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }
                if (IsColorRulePicking)
                {
                    BeginInvoke((Action)(() => ExitColorPicking(true)));
                    return (IntPtr)1;
                }
                if (IsColorManagerVisible)
                {
                    BeginInvoke((Action)CloseColorManager);
                    return (IntPtr)1;
                }
                Logger.Debug($"[hook] ESC pressed, stopping all");
                BeginInvoke((Action)(() =>
                {
                    Logger.Debug("[hook] ESC BeginInvoke: calling StopAll/CancelAutoAll/CancelIsland");
                    _state.StopAll();
                    CancelAutoAll();
                    CancelIsland();
                    SetOperationControlsEnabled(true);
                    Logger.Debug("[hook] ESC: all stopped, restoring overlay if needed");
                    RestorePreviewOverlayIfChecked();
                }));
                // 填充状态下只保留 ESC 的响应，不再把 ESC 透传给其他窗口
                return (IntPtr)1;
            }

            if (operationActive)
            {
                // 填充/检测/补涂过程中，除 ESC 外的所有按键均被屏蔽
                Logger.Debug($"[hook] key {vkCode} suppressed during active operation");
                return (IntPtr)1;
            }

            // 鐩墠S閿嚜鍔ㄦ粦鍔ㄦ娴嬬簿搴︽瀬鍏朵笉鍑嗙‘锛屾殏鏃跺叧闂?
            // if (vkCode == NativeMethods.VK_S)
            // {
            //     BeginInvoke((Action)(() =>
            //     {
            //         bool enabled = _state.ToggleAction();
            //         Logger.Debug($"[toggle] enabled={enabled}");
            //     }));
            // }
            if (vkCode == NativeMethods.VK_A && !injected)
            {
                var triggerPosition = Cursor.Position;
                var triggeredAtMs = Environment.TickCount64;
                if (QueueColorRulePick(triggerPosition, triggeredAtMs))
                {
                    return (IntPtr)1;
                }
                if (IsColorManagerVisible)
                {
                    Logger.Debug("[hook] A suppressed while color manager is open outside pick mode");
                    return (IntPtr)1;
                }
                Logger.Debug($"[hook] A pressed, recording color");
                BeginInvoke((Action)(() =>
                {
                    // 光标位于本程序窗口上时按 A：取色无意义，且此刻本程序窗口往往是前台，
                    // 后续发送的 I 键 / 左键会被本程序吞掉，或左键直接点到自己的按钮上，
                    // 表现为“按 A 却触发其他操作”。直接中止这次取色。
                    var currentPosition = Cursor.Position;
                    Logger.Debug($"[hook] A handling trigger=({triggerPosition.X},{triggerPosition.Y}) current=({currentPosition.X},{currentPosition.Y}) delayMs={Math.Max(0, Environment.TickCount64 - triggeredAtMs)}");
                    if (IsCursorOverSelf(triggerPosition))
                    {
                        Logger.Debug($"[hook] A ignored: trigger position over self at ({triggerPosition.X},{triggerPosition.Y})");
                        return;
                    }
                    PerformColorRecordAndAction(triggerPosition);
                }));
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private (BgrColor hover, BgrColor clear, bool stable, int waitMs, int reads, bool changedFromHover) CaptureColorSamples(Point samplePosition)
    {
        Logger.Debug($"[record] CaptureColorSamples start pos=({samplePosition.X},{samplePosition.Y})");
        // 取色前隐藏覆盖层，避免读到覆盖层的颜色
        HidePreviewOverlay();
        try
        {
            BgrColor hover;
            BgrColor clear;
            using (var dc = new ScreenDc())
            {
                hover = dc.GetPixel(samplePosition.X, samplePosition.Y);
            }
            Logger.Debug($"[record] hover at ({samplePosition.X},{samplePosition.Y}) rgb=[{hover.R},{hover.G},{hover.B}]");
            var safe = PickSafePos(_state.Snapshot().recordedRange, samplePosition);
            bool cursorMoved = false;
            if (safe.HasValue)
            {
                Logger.Debug($"[record] moving cursor to safe pos ({safe.Value.X},{safe.Value.Y})");
                cursorMoved = NativeMethods.SetCursorPos(safe.Value.X, safe.Value.Y);
            }

            clear = hover;
            int stableReads = 0;
            int reads = 0;
            bool stable = false;
            bool changedFromHover = false;
            long settleStart = Environment.TickCount64;
            var previous = hover;
            int hoverPaletteDiff = NormalizePaletteColor(hover).diff;
            if (cursorMoved)
            {
                using var dc = new ScreenDc();
                while (Environment.TickCount64 - settleStart < ClearSampleTimeoutMs)
                {
                    Thread.Sleep(ClearSamplePollMs);
                    clear = dc.GetPixel(samplePosition.X, samplePosition.Y);
                    reads++;
                    if (clear.MaxDiff(previous) <= ClearSampleStableTolerance)
                    {
                        stableReads++;
                    }
                    else
                    {
                        stableReads = 1;
                    }
                    previous = clear;
                    changedFromHover = clear.MaxDiff(hover) > ClearSampleStableTolerance;
                    int waitMs = (int)Math.Max(0, Environment.TickCount64 - settleStart);
                    int paletteDiff = NormalizePaletteColor(clear).diff;
                    bool hasClearEvidence = changedFromHover || hoverPaletteDiff <= 2;
                    if (waitMs >= ClearSampleMinWaitMs &&
                        stableReads >= ClearSampleStableReads &&
                        paletteDiff <= ClearSamplePaletteTolerance &&
                        hasClearEvidence)
                    {
                        stable = true;
                        break;
                    }
                }
            }
            int elapsedMs = (int)Math.Max(0, Environment.TickCount64 - settleStart);
            Logger.Debug($"[record] clear at ({samplePosition.X},{samplePosition.Y}) rgb=[{clear.R},{clear.G},{clear.B}] stable={stable} changed={changedFromHover} waitMs={elapsedMs} reads={reads} moved={cursorMoved}");

            _state.RecordColors(new List<BgrColor> { hover, clear }, new List<BgrColor> { hover, clear }, samplePosition);
            Logger.Debug($"[record] raw_colors_rgb=[{string.Join(",", hover.ToRgbArray())}],[{string.Join(",", clear.ToRgbArray())}] pos=({samplePosition.X},{samplePosition.Y})");
            return (hover, clear, stable, elapsedMs, reads, changedFromHover);
        }
        finally
        {
            NativeMethods.SetCursorPos(samplePosition.X, samplePosition.Y);
        }
    }

    private (BgrColor hover, BgrColor clear) PerformColorRecordAndAction()
    {
        return PerformColorRecordAndAction(Cursor.Position);
    }

    private (BgrColor hover, BgrColor clear) PerformColorRecordAndAction(Point samplePosition)
    {
        Logger.Debug("[record] PerformColorRecordAndAction start");
        var recorded = CaptureColorSamples(samplePosition);
        FocusTargetUnderCursor();
        Logger.Debug("[action] send key I");
        Thread.Sleep(50);
        SendKey(NativeMethods.VK_I);
        Thread.Sleep(50);
        Logger.Debug("[action] click left");
        ClickCurrentPosition();
        Logger.Debug("[record] PerformColorRecordAndAction done");
        return (recorded.hover, recorded.clear);
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

    private void CancelIsland()
    {
        var cts = Interlocked.Exchange(ref _islandCts, null);
        if (cts != null)
        {
            Logger.Debug("[cancel] CancelIsland: cancelling and disposing islandCts");
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
            IslandConfirmDialog.Show(this, "请框选范围", "提示", false);
            return;
        }

        var htmlColors = GetPredefinedColors();
        if (htmlColors.Count == 0)
        {
             Logger.Debug("[auto_all] no predefined colors found");
             IslandConfirmDialog.Show(this, "未找到颜色定义", "错误", false);
             return;
        }
        var allowedColors = _colorRules.GetEffectiveColors();
        if (allowedColors.Count == 0)
        {
            Logger.Debug("[auto_all] color rules exclude every built-in color");
            IslandConfirmDialog.Show(this, "当前颜色规则没有留下任何可填颜色，请先打开颜色管理进行调整。", "提示", false);
            return;
        }
        Logger.Debug($"[auto_all] palette={htmlColors.Count} allowed={allowedColors.Count} range={snapshot.recordedRange.Value}");

        var workers = ReadScanWorkers();
        _options.ScanWorkers = workers;
        if (textCores.Text != workers.ToString())
        {
            textCores.Text = workers.ToString();
        }

        SetOperationControlsEnabled(false);
        HidePreviewOverlay();

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
                // 始终先按完整 63 色色板判色，再应用允许集合。这样被排除色不会因容差而误归到相邻的允许色。
                var groups = ScanMatchingPointsForAllColors(snapshot.recordedRange.Value, htmlColors, allowedColors, token, _state.GetPolygon());
                Logger.Debug($"[auto_all] scan returned groups={groups.Count} cancelled={token.IsCancellationRequested}");
                if (token.IsCancellationRequested) return;

                int totalPoints = groups.Values.Sum(list => list.Count);
                Logger.Debug($"[auto_all] scan done. total_points={totalPoints}");

                BeginInvoke((Action)(() => {
                    _autoAllProgressCurrent = 0;
                    _autoAllProgressTotal = totalPoints;
                    _autoAllProgressStart = DateTime.Now;
                    _autoAllProgressActive = true;
                    progressAutoAll.Maximum = Math.Max(1, totalPoints);
                    progressAutoAll.Value = 0;
                    labelAutoAllValue.Text = $"0 / {totalPoints}";
                }));

                Logger.Debug($"[auto_all] executing fill for {totalPoints} points...");
                ExecuteAutoFillAll(groups, allowedColors, token);
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
                    SetOperationControlsEnabled(true);
                    RestorePreviewOverlayIfChecked();
                    var cts = Interlocked.Exchange(ref _autoAllCts, null);
                    cts?.Dispose();
                    Logger.Debug("[auto_all] task cleanup done");
                }));
            }
        }, token);
    }

    // ==================== 遗漏点孤岛检测 ====================

    /// <summary>
    /// 扫描框选范围，返回带标签的网格点列表：(屏幕坐标, 匹配到的预设色)。
    /// 不匹配任何预设色的点不出现在结果中——孤岛检测据此把它们视作"护城河"。
    /// 骨架与 ScanMatchingPointsForAllColors 一致，但保留逐点 (Point, Color) 便于空间拓扑分析。
    /// </summary>
    /// <summary>
    /// 扫描框选范围，返回完整采样网格：hex key → 标签。
    /// value 为 null 表示该采样点未匹配任何预设色（护城河来源）；非 null 表示匹配到该色。
    /// 必须包含所有采样点（含未匹配），孤岛检测的护城河判定依赖“采样了但没匹配”的点。
    /// 用 (col,row) 六边形坐标作 key，与 ScanPattern 的品字形采样严格对应。
    /// </summary>
    private Dictionary<long, BgrColor?> ScanLabeledGrid(Rectangle rect, List<BgrColor> targets, CancellationToken token, List<Point>? polygon = null)
    {
        var poly = polygon != null && polygon.Count >= 3 ? new OrthogonalPolygon(polygon) : null;
        Logger.Debug($"[scan_island] ScanLabeledGrid rect={rect} targets={targets.Count} tol={_options.IslandColorTol}(island) step={_options.ScanStep} workers={_options.ScanWorkers}");

        int width = Math.Max(1, rect.Width);
        int height = Math.Max(1, rect.Height);
        int step = _options.ScanStep;
        int countX = ((width - 1) / step) + 1;
        int countY = ((height - 1) / step) + 1;
        int total = Math.Max(0, countX * countY);
        _state.StartScan(total);
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

        int stride = data.Stride;
        int islandTol = _options.IslandColorTol;
        BgrColor ReadBufferPixel(int x, int y)
        {
            int offset = (y * stride) + (x * 4);
            byte b = buffer[offset];
            byte g = buffer[offset + 1];
            byte r = buffer[offset + 2];
            return new BgrColor(b, g, r);
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

        var grid = new ConcurrentDictionary<long, BgrColor?>();
        int matchedCount = 0;
        try
        {
            Parallel.For(0, countY,
                new ParallelOptions { MaxDegreeOfParallelism = _options.ScanWorkers, CancellationToken = token },
                (row) =>
                {
                    int y = row * step;
                    if (y >= height)
                    {
                        return;
                    }
                    int startOffset = (row % 2 == 1) ? (step / 2) : 0;
                    int localMatched = 0;
                    for (int col = 0; col < countX; col++)
                    {
                        int x = col * step + startOffset;
                        if (x >= width)
                        {
                            break;
                        }
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                        if (poly != null && !poly.Contains(rect.Left + x, rect.Top + y))
                        {
                            continue;
                        }
                        var bgr = ReadBufferPixel(x, y);
                        int localMin = int.MaxValue;
                        BgrColor? bestTarget = null;
                        foreach (var t in targets)
                        {
                            int diff = bgr.MaxDiff(t);
                            if (diff < localMin)
                            {
                                localMin = diff;
                                bestTarget = t;
                            }
                        }
                        BgrColor? label = (bestTarget.HasValue && localMin <= islandTol) ? bestTarget : null;
                        grid[IslandDetector.ToHexKey(col, row)] = label;
                        if (label.HasValue)
                        {
                            localMatched++;
                        }
                    }
                    Interlocked.Add(ref matchedCount, localMatched);
                    var newDone = Interlocked.Add(ref done, countX);
                    MaybeUpdateProgress(newDone);
                });
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("[scan_island] canceled");
        }
        _state.SetScanProgress(total, done);

        var result = new Dictionary<long, BgrColor?>(grid);
        Logger.Debug($"[scan_island] scanned={result.Count} matched={matchedCount} (moat sources={result.Count - matchedCount})");
        return result;
    }

    /// <summary>
    /// 遗漏点检测主流程：扫描建图 → 孤岛判定 → 标注为待处理点 → 弹窗确认 → 复用全自动填涂补涂。
    /// 在后台 Task 中运行，所有 UI 操作通过 BeginInvoke 切回 UI 线程。
    /// </summary>
    private void RunIslandDetection(Rectangle rect, List<BgrColor> allowedColors, CancellationToken token, List<Point>? polygon = null)
    {
        Logger.Debug("[island] RunIslandDetection start");
        var targets = GetPredefinedColors();
        if (targets.Count == 0)
        {
            BeginInvoke((Action)(() => IslandConfirmDialog.Show(this, "未找到颜色定义", "错误", false)));
            return;
        }

        // 1) 扫描建图
        var labeled = ScanLabeledGrid(rect, targets, token, polygon);
        if (token.IsCancellationRequested)
        {
            return;
        }

        // 2) 孤岛判定：小簇 + 护城河 + 外围同色大簇
        var detectedIslands = IslandDetector.Detect(
            labeled,
            rect,
            _options.ScanStep,
            _options.IslandMaxSize,
            _options.IslandMoatRatio,
            _options.IslandRequireOuterBig,
            _options.IslandSearchRadius,
            _options.IslandMinOuterMultiplier,
            _options.IslandStrongMoatRatio);
        var allowedSet = new HashSet<BgrColor>(allowedColors);
        var islands = detectedIslands.Where(island => allowedSet.Contains(island.Color)).ToList();
        int totalMissed = islands.Sum(i => i.Points.Count);
        Logger.Debug($"[island] detected={detectedIslands.Count} allowed={islands.Count} totalMissed={totalMissed}");

        // 诊断：无论是否找到遗漏点，都输出网格分布，用于定位“测不出”根因
        if (_options.IslandDiagnose)
        {
            var diag = IslandDetector.Diagnose(labeled, _options.IslandMaxSize);
            foreach (var line in diag.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Logger.Debug($"[diag] {line}");
            }

            if (islands.Count == 0 || totalMissed == 0)
            {
                Logger.Debug("[island] no missed points found");
                var msg = "当前颜色规则下未检测到遗漏点。\n\n诊断信息：\n" + diag;
                BeginInvoke((Action)(() => IslandConfirmDialog.Show(this, msg, "遗漏检测", false)));
                return;
            }
        }
        else if (islands.Count == 0 || totalMissed == 0)
        {
            Logger.Debug("[island] no missed points found");
            BeginInvoke((Action)(() => IslandConfirmDialog.Show(this, "当前颜色规则下未检测到遗漏点", "遗漏检测", false)));
            return;
        }

        // 3) 按颜色分组 + 聚类排序，供覆盖层显示与补涂复用
        var groups = new Dictionary<BgrColor, List<Point>>();
        foreach (var isl in islands)
        {
            if (!groups.TryGetValue(isl.Color, out var list))
            {
                list = new List<Point>();
                groups[isl.Color] = list;
            }
            list.AddRange(isl.Points);
        }
        var orderedMissed = new List<Point>();
        foreach (var kv in groups)
        {
            var clusters = FillPlanner.ClusterPoints(kv.Value, _options.ScanStep, _options.ClusterNeighborDistance);
            orderedMissed.AddRange(FillPlanner.FlattenClusters(clusters));
        }

        // 4) 重新标注为待处理点（覆盖层显示），让用户能看着遗漏点确认
        BeginInvoke((Action)(() => SetOverlayFillPoints(orderedMissed, true)));

        // 5) 弹窗确认（必须在 UI 线程；IslandConfirmDialog.ShowDialog 会阻塞至用户选择）
        bool confirm = false;
        using var done = new ManualResetEventSlim(false);
        BeginInvoke((Action)(() =>
        {
            var r = IslandConfirmDialog.Show(this,
                $"检测到 {islands.Count} 处孤岛（{totalMissed} 个像素点）。\n请确认这些点是否需要补涂，确认后将自动补涂。",
                "遗漏检测", true);
            confirm = r == DialogResult.Yes;
            done.Set();
        }));
        done.Wait(token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        if (!confirm)
        {
            Logger.Debug("[island] user declined, skipping refill");
            BeginInvoke((Action)(() => ClearOverlayFill()));
            return;
        }

        // 6) 补涂：复用全自动填涂流程（白色优先），ExecuteAutoFillAll 会自管覆盖层/进度/取色/空格
        var order = groups.Keys
            .Where(IsWhite)
            .Concat(groups.Keys.Where(c => !IsWhite(c)))
            .ToList();
        Logger.Debug($"[island] refilling {totalMissed} points across {groups.Count} colors");
        ExecuteAutoFillAll(groups, order, token);
        Logger.Debug("[island] refill done");
    }

    /// <summary>
    /// 遗漏检测入口（手动按钮 & 自动 hook 共用）：校验范围、禁用 UI、启动后台检测 Task。
    /// </summary>
    private void BeginIslandDetection()
    {
        Logger.Debug("[ui] BeginIslandDetection");
        if (!btnRunIslandDetect.Enabled)
        {
            Logger.Debug("[island] button disabled, skipping");
            return;
        }
        var snapshot = _state.Snapshot();
        if (!snapshot.recordedRange.HasValue)
        {
            Logger.Debug("[island] no range, showing message box");
            IslandConfirmDialog.Show(this, "请框选范围", "提示", false);
            return;
        }
        var allowedColors = _colorRules.GetEffectiveColors();
        if (allowedColors.Count == 0)
        {
            Logger.Debug("[island] color rules exclude every built-in color");
            IslandConfirmDialog.Show(this, "当前颜色规则没有留下任何可处理颜色，请先打开颜色管理进行调整。", "提示", false);
            return;
        }

        if (!_settings.SkipIslandRecommendation)
        {
            var (recommendResult, dontShow) = IslandConfirmDialog.ShowWithDontShowAgain(
                this,
                "强烈建议在已经填涂好的情况下进行遗漏检测功能。\n\n是否继续运行遗漏检测？",
                "遗漏检测",
                "不再提示");
            if (dontShow)
            {
                _settings.SkipIslandRecommendation = true;
            }
            if (recommendResult != DialogResult.Yes)
            {
                Logger.Debug("[island] user declined recommendation");
                return;
            }
        }
        // 已设“不再提示”时直接继续（视作继续），不再弹框

        _options.ScanStep = ReadScanStep();
        if (ScanStep.Text != _options.ScanStep.ToString())
        {
            ScanStep.Text = _options.ScanStep.ToString();
        }
        var workers = ReadScanWorkers();
        _options.ScanWorkers = workers;
        if (textCores.Text != workers.ToString())
        {
            textCores.Text = workers.ToString();
        }

        SetOperationControlsEnabled(false);
        HidePreviewOverlay();

        CancelAutoAll();
        _islandCts?.Dispose();
        _islandCts = new CancellationTokenSource();
        var token = _islandCts.Token;
        _autoAllProgressCurrent = 0;
        _autoAllProgressTotal = 0;
        _autoAllProgressStart = DateTime.Now;
        _autoAllProgressActive = true;
        Logger.Debug("[island] starting detection task...");

        Task.Run(() =>
        {
            try
            {
                RunIslandDetection(snapshot.recordedRange.Value, allowedColors, token, _state.GetPolygon());
            }
            catch (Exception ex)
            {
                Logger.Error($"[island] error: {ex}");
            }
            finally
            {
                BeginInvoke((Action)(() =>
                {
                    _autoAllProgressActive = false;
                    SetOperationControlsEnabled(true);
                    RestorePreviewOverlayIfChecked();
                    var cts = Interlocked.Exchange(ref _islandCts, null);
                    cts?.Dispose();
                    Logger.Debug("[island] task cleanup done");
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

    private Dictionary<BgrColor, List<Point>> ScanMatchingPointsForAllColors(
        Rectangle rect,
        List<BgrColor> targets,
        List<BgrColor> allowedTargets,
        CancellationToken token,
        List<Point>? polygon = null)
    {
        var poly = polygon != null && polygon.Count >= 3 ? new OrthogonalPolygon(polygon) : null;
        Logger.Debug($"[scan_all] ScanMatchingPointsForAllColors rect={rect} palette={targets.Count} allowed={allowedTargets.Count}");
        var allowedSet = new HashSet<BgrColor>(allowedTargets);
        var groups = new ConcurrentDictionary<BgrColor, ConcurrentBag<Point>>();
        foreach (var color in allowedTargets)
        {
            groups[color] = new ConcurrentBag<Point>();
        }

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
                if (poly != null && !poly.Contains(rect.Left + x, rect.Top + y)) continue;

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

                if (bestTarget.HasValue && localMin <= _options.ColorTol && allowedSet.Contains(bestTarget.Value))
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
        BeginInvoke((Action)(() =>
        {
            _autoAllProgressCurrent = 0;
            _autoAllProgressTotal = total;
            _autoAllProgressStart = _autoFillAllStartTime;
            _autoAllProgressActive = total > 0;
            progressAutoAll.Maximum = Math.Max(1, total);
            progressAutoAll.Value = 0;
            labelAutoAllValue.Text = $"0 / {total}";
        }));
        var orderedColors = order
            .Where(IsWhite)
            .Concat(order.Where(color => !IsWhite(color)))
            .ToList();
        bool whiteColorCompleted = false;
        // 本次自动化运行中已经成功取色（并用作笔色）的预设色集合。
        // 取色时若读到的底色命中此集合，说明该点已是之前涂过的颜色（已涂区域/串色），
        // 跳过本次填涂并移至下一点重新取色，直至取到未用过的颜色，避免用旧笔色覆盖当前色组。
        var pickedColors = new HashSet<BgrColor>();

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

                // 取色异常：读到的底色命中本次运行已取过的颜色（多为已涂区域/串色），并非当前目标色。
                // 跳过本次填涂，移至下一点重新取色，以此循环，直至取到未用过的颜色。
                // 注意：白色样本已由上面的 white-skip 分支提前处理（常见情况，仅记 Debug，避免污染错误日志），
                //       此处只命中非白色的“旧笔色”，属于真正需要记录的取色异常。
                if (IsColorAlreadyPicked(recorded.clear, pickedColors))
                {
                    Logger.Error($"[auto_all] 取色异常：目标色 {FormatBgr(color)} 在点 ({first.X},{first.Y}) 取到已用过的颜色 {FormatBgr(recorded.clear)}，跳过填涂并移至下一点重新取色");
                    processed++;
                    UpdateAutoAllOverlay(processed);
                    UpdateAutoAllProgress(processed, total);
                    startIndex++;
                    continue;
                }

                Thread.Sleep(_options.ColorPickToFillDelayMs);
                SendSpace();
                // 取色并涂色成功：将当前目标色记入已取色集合，供后续色组比对
                pickedColors.Add(color);
                processed++;
                UpdateAutoAllOverlay(processed);
                UpdateAutoAllProgress(processed, total);
                startIndex++;
                firstPointHandled = true;
                break;
            }

            if (!firstPointHandled)
            {
                // 整个色组的所有点都取到已用过的颜色（或白色），未找到可用作取色的点
                Logger.Error($"[auto_all] 取色异常：颜色 {FormatBgr(color)} 的 {orderedPoints.Count} 个点均取到已用过的颜色，无法取到目标色，跳过该色组");
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
                progressAutoAll.Maximum = Math.Max(1, total);
                progressAutoAll.Value = Math.Min(current, progressAutoAll.Maximum);
                labelAutoAllValue.Text = $"{current} / {total}{GetEta(_autoFillAllStartTime, current, total)}";
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
        int S(int value) => (int)Math.Round(
            value * DeviceDpi / (double)LayoutDesignDpi,
            MidpointRounding.AwayFromZero);
        Point P(int x, int y) => new Point(S(x), S(y));
        Size Z(int width, int height) => new Size(S(width), S(height));

        SuspendLayout();
        try
        {
            labelRec.Location = P(4, 4);
            _compactDivider1.Width = S(2);
            _compactDivider2.Width = S(2);
            _colorManagerButton.Visible = true;

            if (mode == UiLayoutMode.Vertical)
            {
                ClientSize = Z(383, 505);
                _compactDivider1.Visible = false;
                _compactDivider2.Visible = false;
                color1.Visible = true;
                color2.Visible = true;
                label1.Visible = false;
                label2.Visible = false;
                label4.Visible = false;
                label5.Visible = false;
                label6.Visible = false;
                label8.Visible = false;
                label10.Visible = false;
                TheRange.Visible = true;
                checkShowRange.Visible = true;
                panelLeft.Location = P(12, 56);
                panelLeft.Size = Z(80, 30);
                panelRight.Location = P(198, 56);
                panelRight.Size = Z(80, 30);
                labelCores.Text = "调用CPU数量";
                btnAutoCores.Text = "自动决定CPU数量";
                label7.Text = "扫描步长";
                btnRange.Text = "划取检测范围";
                btnAutoFillAll.Text = "全自动检测及填充";
                color1.Location = P(12, 34);
                color2.Location = P(198, 34);
                label3.Location = P(12, 8);
                _colorManagerButton.Location = P(12, 94);
                _colorManagerButton.Size = Z(351, 38);
                labelCores.Location = P(12, 145);
                textCores.Location = P(119, 142);
                textCores.Size = Z(40, 27);
                btnAutoCores.Location = P(10, 176);
                btnAutoCores.Size = Z(142, 26);
                label7.Location = P(14, 210);
                ScanStep.Location = P(89, 207);
                ScanStep.Size = Z(69, 27);
                btnRange.Location = P(12, 242);
                btnRange.Size = Z(122, 26);
                RangeRecord.Location = P(12, 274);
                TheRange.Location = P(113, 274);
                checkShowRange.Location = P(12, 296);
                btnAutoFillAll.Location = P(12, 330);
                btnAutoFillAll.Size = Z(160, 26);
                radioSpeedBalanced.Text = "平衡";
                radioSpeedBalanced.Location = P(178, 333);
                radioSpeedExtreme.Text = "极致速度";
                radioSpeedExtreme.Location = P(252, 333);
                radioSpeedBalanced.Visible = true;
                radioSpeedExtreme.Visible = true;
                btnRunIslandDetect.Text = "运行遗漏检测";
                btnRunIslandDetect.Location = P(12, 364);
                btnRunIslandDetect.Size = Z(160, 26);
                btnRunIslandDetect.Visible = true;
                btnToggleLayout.Location = P(178, 364);
                btnToggleLayout.Size = Z(145, 26);
                labelAutoAll.Location = P(17, 399);
                labelAutoAllValue.Location = P(128, 399);
                progressAutoAll.Location = P(12, 421);
                progressAutoAll.Size = Z(351, 24);
                labelCurrentVersion.Location = P(12, 454);
                linkGithubOrUpdate.Location = P(12, 476);
                btnToggleLayout.Text = "切换为精简布局";
            }
            else
            {
                ClientSize = Z(CompactLayoutWidth, CompactLayoutHeight);
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

                // 区域1：颜色/CPU/步长/划取
                label3.Location = P(10, 8);
                panelLeft.Location = P(10, 39);
                panelLeft.Size = Z(48, 24);
                panelRight.Location = P(68, 39);
                panelRight.Size = Z(48, 24);
                color1.Location = P(10, 33);
                color2.Location = P(128, 33);
                _colorManagerButton.Location = P(10, 72);
                _colorManagerButton.Size = Z(224, 34);

                labelCores.Text = "cpu";
                labelCores.Location = P(248, 32);
                textCores.Location = P(286, 29);
                textCores.Size = Z(40, 27);
                btnAutoCores.Text = "自动";
                btnAutoCores.Location = P(332, 29);
                btnAutoCores.Size = Z(56, 26);

                label7.Text = "步长";
                label7.Location = P(248, 66);
                ScanStep.Location = P(286, 63);
                ScanStep.Size = Z(60, 27);

                btnRange.Text = "划取";
                btnRange.Location = P(248, 98);
                btnRange.Size = Z(58, 26);
                RangeRecord.Location = P(312, 102);
                checkShowRange.Location = P(248, 128);
                checkShowRange.Visible = true;

                // 区域2：动作按钮 + 进度
                btnAutoFillAll.Text = "全自动";
                btnAutoFillAll.Location = P(430, 20);
                btnAutoFillAll.Size = Z(100, 26);
                radioSpeedBalanced.Text = "平衡";
                radioSpeedBalanced.Location = P(430, 54);
                radioSpeedExtreme.Text = "极致";
                radioSpeedExtreme.Location = P(492, 54);
                radioSpeedBalanced.Visible = true;
                radioSpeedExtreme.Visible = true;
                btnRunIslandDetect.Text = "遗漏";
                btnRunIslandDetect.Location = P(536, 20);
                btnRunIslandDetect.Size = Z(76, 26);
                btnRunIslandDetect.Visible = true;

                labelAutoAll.Location = P(430, 82);
                labelAutoAllValue.Location = P(500, 82);
                progressAutoAll.Location = P(430, 106);
                progressAutoAll.Size = Z(CompactLayoutWidth - 450, 22);

                // 区域1底部：入口
                labelCurrentVersion.Location = P(10, 124);
                linkGithubOrUpdate.Location = P(10, 146);
                btnToggleLayout.Location = P(CompactLayoutWidth - 152, CompactLayoutHeight - 38);
                btnToggleLayout.Size = Z(145, 26);
                btnToggleLayout.Text = "切换为完整布局";

                _compactDivider1.Location = P(410, 16);
                _compactDivider1.Height = S(CompactLayoutHeight - 40);
            }

            LayoutColorManagementUi();
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }

        // AutoSize labels can become a few pixels wider with a different CJK
        // font or with Windows' text-size accessibility setting.  Keep a small
        // trailing margin instead of clipping any such control at the form edge.
        int margin = S(7);
        int requiredWidth = Controls.Cast<Control>()
            .Where(control => control.Visible)
            .Select(control => control.Right)
            .DefaultIfEmpty(0)
            .Max() + margin;
        int requiredHeight = Controls.Cast<Control>()
            .Where(control => control.Visible)
            .Select(control => control.Bottom)
            .DefaultIfEmpty(0)
            .Max() + margin;
        ClientSize = new Size(
            Math.Max(ClientSize.Width, requiredWidth),
            Math.Max(ClientSize.Height, requiredHeight));
    }

    private async Task CheckLatestReleaseAsync()
    {
        var result = await ReleaseUpdateChecker.CheckAsync();
        if (IsDisposed || Disposing) return;
        var latestTag = result.Tag;
        if (string.IsNullOrEmpty(latestTag))
        {
            Logger.Warning($"[update] 本次自动检查更新失败，不影响取色和填涂，可稍后手动访问发布页。{result.Failure}");
            _updateTargetUrl = ReleaseUpdateChecker.LatestReleaseUrl;
            linkGithubOrUpdate.Text = "暂未检查更新，点击查看发布页";
            return;
        }

        if (!TryParseVersion(latestTag, out var latestVersion) || latestVersion <= _currentAppVersion)
        {
            return;
        }

        _updateTargetUrl = $"{RepoUrl}/releases/tag/{Uri.EscapeDataString(latestTag)}";
        linkGithubOrUpdate.Text = $"发现新版本 {latestTag}，点击下载";
        linkGithubOrUpdate.LinkColor = Color.OrangeRed;
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

        private void label8_Click(object sender, EventArgs e)
        {

        }
    }
}



