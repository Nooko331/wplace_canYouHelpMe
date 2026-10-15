using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace WplaceColorWatch
{
    public partial class Form1
    {
        private readonly WindowsEngineHost? _desktopHost;
        private int _desktopTasks;
        // Layout is session-only; every new engine starts in full mode.
        private bool _desktopCompact;
        private string _desktopStatus = "就绪";
        internal WindowsEngineHost? DesktopHost => _desktopHost;
        private bool DesktopBusy => _desktopTasks > 0 || IsOperationActive() || _selectingRange;

        private bool IsDesktopApplicationForeground()
        {
            NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint processId);
            return processId == (uint)Environment.ProcessId;
        }

        internal void PublishDesktopSnapshot()
        {
            if (_desktopHost == null || _closing) return;
            var snapshot = _state.Snapshot();
            bool fill = _autoAllProgressTotal > 0;
            int total = fill ? _autoAllProgressTotal : snapshot.scanTotal;
            int current = Math.Clamp(fill ? _autoAllProgressCurrent : snapshot.scanDone, 0, Math.Max(0, total));
            var started = fill ? _autoAllProgressStart : snapshot.scanStartTime;
            string eta = "--:--";
            if (DesktopBusy && current > 0 && total > current && started != default)
            {
                var seconds = (DateTime.Now - started).TotalSeconds;
                if (seconds >= 1)
                {
                    var remaining = TimeSpan.FromSeconds(Math.Clamp(seconds / current * (total - current), 0, 359999));
                    eta = remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}" : remaining.ToString(@"mm\:ss");
                }
            }
            else if (total > 0 && current == total) eta = "00:00";
            string range = "尚未划取范围";
            if (snapshot.recordedRange is Rectangle rect)
                range = $"{(_state.GetPolygon() == null ? "矩形" : "正交多边形")} · {rect.Width} × {rect.Height} px · ({rect.X}, {rect.Y})";
            var status = _desktopStatus;
            if (DesktopBusy && status != "已停止") status = fill ? "正在填涂" : _islandCts != null ? "正在检测遗漏" : "正在扫描";
            if (_desktopHost.PromptOpen) status = "等待确认";
            _desktopHost.Publish(new DesktopSnapshot
            {
                Ready = true, Busy = DesktopBusy, SelectingRange = _selectingRange,
                ColorsOpen = IsColorManagerVisible, Picking = IsColorRulePicking ? _colorPickTarget.ToString() : "",
                PickMessage = _pickCount.Text, Status = status,
                Workers = _options.ScanWorkers, Step = _options.ScanStep, ShowRange = checkShowRange.Checked,
                Extreme = radioSpeedExtreme.Checked, Compact = _desktopCompact,
                HoverColor = snapshot.recordedBgrsRaw.Count > 0 ? WindowsEngineHost.ToHex(snapshot.recordedBgrsRaw[0]) : null,
                ClearColor = snapshot.recordedBgrsRaw.Count > 1 ? WindowsEngineHost.ToHex(snapshot.recordedBgrsRaw[1]) : null,
                HasRange = snapshot.recordedRange.HasValue, Range = range,
                Current = current, Total = total, Percent = total > 0 ? (int)(100L * current / total) : 0, Remaining = eta,
                Wanted = _colorRules.GetWanted().Select(WindowsEngineHost.ToHex).ToArray(),
                Excluded = _colorRules.GetExcluded().Select(WindowsEngineHost.ToHex).ToArray(),
                EffectiveCount = _colorRules.GetEffectiveColors().Count
            });
        }

        internal void SetDesktopParameters(int workers, int step, bool showRange, bool extreme)
        {
            if (DesktopBusy || _desktopHost?.PromptOpen == true) return;
            if (workers < 1 || workers > Math.Max(1, Environment.ProcessorCount - 1) || step < 1)
                throw new ArgumentOutOfRangeException(nameof(step), "请输入有效的 CPU 数量和正整数步长。");
            _options.ScanWorkers = workers;
            _options.ScanStep = step;
            textCores.Text = workers.ToString();
            ScanStep.Text = step.ToString();
            radioSpeedExtreme.Checked = extreme;
            radioSpeedBalanced.Checked = !extreme;
            checkShowRange.Checked = showRange;
            if (showRange && _desktopHost?.ReadOnly != true) RefreshRangePreview();
        }
        internal void SetDesktopCompact(bool compact) => _desktopCompact = compact;

        internal void ExecuteDesktopCommand(DesktopCommand command, string? color)
        {
            if (command == DesktopCommand.Stop)
            {
                _state.StopAll(); CancelAutoAll(); CancelIsland();
                _desktopStatus = "已停止";
                if (_desktopTasks == 0) SetOperationControlsEnabled(true);
                RestorePreviewOverlayIfChecked();
                return;
            }
            if (command == DesktopCommand.StopPicking || command == DesktopCommand.CloseColors)
            {
                ExitColorPicking(command != DesktopCommand.CloseColors);
                if (command == DesktopCommand.CloseColors) _colorManagerOpen = false;
                return;
            }
            if (DesktopBusy || _desktopHost?.PromptOpen == true) return;
            if (_desktopHost?.ReadOnly == true && (command == DesktopCommand.SelectRange || command == DesktopCommand.StartFill || command == DesktopCommand.DetectMissing || command == DesktopCommand.PickWanted || command == DesktopCommand.PickExcluded))
            {
                _desktopHost.Confirm(new EnginePrompt("界面预览", "当前以界面预览启动，未启用屏幕读取、全局快捷键和填涂。正常打开 EXE 即可使用完整功能。", false, null));
                return;
            }
            switch (command)
            {
                case DesktopCommand.SelectRange:
                    _desktopHost?.SetSelectingRange(true);
                    try { BeginRangeSelect(); _desktopStatus = "就绪"; }
                    finally { _desktopHost?.SetSelectingRange(false); }
                    break;
                case DesktopCommand.StartFill: BeginAutoFillAll(); break;
                case DesktopCommand.DetectMissing: BeginIslandDetection(); break;
                case DesktopCommand.AutoWorkers: AutoDetectCores(); break;
                case DesktopCommand.OpenColors: _colorManagerOpen = true; break;
                case DesktopCommand.PickWanted: _colorManagerOpen = true; EnterColorPicking(ColorPickTarget.Wanted); break;
                case DesktopCommand.PickExcluded: _colorManagerOpen = true; EnterColorPicking(ColorPickTarget.Excluded); break;
                case DesktopCommand.SelectAllColors: _colorRules.SelectAllWanted(); break;
                case DesktopCommand.ClearWanted: _colorRules.ClearWanted(); break;
                default:
                    if (color == null || !WindowsEngineHost.Palette.Contains(color, StringComparer.OrdinalIgnoreCase)) return;
                    var value = BgrColor.FromColor(ColorTranslator.FromHtml(color));
                    if (command == DesktopCommand.AddWanted) _colorRules.AddWanted(value);
                    if (command == DesktopCommand.AddExcluded) _colorRules.AddExcluded(value);
                    if (command == DesktopCommand.RemoveWanted) _colorRules.RemoveWanted(value);
                    if (command == DesktopCommand.RemoveExcluded) _colorRules.RemoveExcluded(value);
                    break;
            }
            RefreshColorRuleUi();
        }

        internal void ShutdownDesktopEngine()
        {
            _state.StopAll(); CancelAutoAll(); CancelIsland(); Unhook();
            updateTimer.Stop();
            var drain = new System.Windows.Forms.Timer { Interval = 30 };
            drain.Tick += (_, _) =>
            {
                if (_desktopTasks > 0 || _selectingRange) return;
                drain.Stop(); drain.Dispose();
                CleanupResources();
                Close();
            };
            drain.Start();
        }
    }
}
