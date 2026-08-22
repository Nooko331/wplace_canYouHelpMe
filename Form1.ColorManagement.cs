using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace WplaceColorWatch
{

public partial class Form1
{
    private enum ColorPickTarget
    {
        None,
        Wanted,
        Excluded
    }

    private readonly ColorRuleSet _colorRules;
    private readonly ColorManagerButton _colorManagerButton = new();
    private readonly Panel _colorManagerOverlay = new();
    private readonly Panel _colorManagerHome = new();
    private readonly Panel _colorPickPage = new();
    private readonly Panel _wantedGroup = new();
    private readonly Panel _excludedGroup = new();
    private readonly Label _colorManagerTitle = new();
    private readonly Label _colorManagerNote = new();
    private readonly Label _wantedTitle = new();
    private readonly Label _excludedTitle = new();
    private readonly FlowLayoutPanel _wantedSwatches = new();
    private readonly FlowLayoutPanel _excludedSwatches = new();
    private readonly Button _btnCloseColorManager = new();
    private readonly Button _btnPickWanted = new();
    private readonly Button _btnSelectAllWanted = new();
    private readonly Button _btnClearWanted = new();
    private readonly Button _btnPickExcluded = new();
    private readonly Label _pickTitle = new();
    private readonly Label _pickCount = new();
    private readonly FlowLayoutPanel _pickSwatches = new();
    private readonly AnimatedKeyHint _pickAnimation = new();
    private readonly Label _pickHint = new();
    private readonly Button _btnStopColorPicking = new();
    private readonly ToolTip _colorToolTip = new();
    private volatile ColorPickTarget _colorPickTarget;
    private volatile bool _colorManagerOpen;
    private int _colorPickPending;

    private bool IsColorRulePicking => _colorPickTarget != ColorPickTarget.None;
    private bool IsColorManagerVisible => _colorManagerOpen;

    private void InitializeColorManagementUi()
    {
        _colorManagerButton.Click += (_, _) => OpenColorManager();
        Controls.Add(_colorManagerButton);

        _colorManagerOverlay.Visible = false;
        _colorManagerOverlay.BackColor = Color.FromArgb(247, 249, 252);
        _colorManagerOverlay.TabStop = true;
        _colorManagerOverlay.Controls.Add(_colorManagerHome);
        _colorManagerOverlay.Controls.Add(_colorPickPage);
        _colorManagerOverlay.Resize += (_, _) => LayoutColorManagementUi();
        Controls.Add(_colorManagerOverlay);

        _colorManagerHome.BackColor = _colorManagerOverlay.BackColor;
        _colorManagerHome.Controls.Add(_colorManagerTitle);
        _colorManagerHome.Controls.Add(_colorManagerNote);
        _colorManagerHome.Controls.Add(_wantedGroup);
        _colorManagerHome.Controls.Add(_excludedGroup);
        _colorManagerHome.Controls.Add(_btnCloseColorManager);

        _colorManagerTitle.AutoSize = false;
        _colorManagerTitle.Text = "颜色管理";
        _colorManagerTitle.Font = new Font(Font, FontStyle.Bold);
        _colorManagerTitle.TextAlign = ContentAlignment.MiddleLeft;
        _colorManagerNote.AutoSize = false;
        _colorManagerNote.Text = "点击色块可移除；同一种颜色只会保留在最后选择的一类中。";
        _colorManagerNote.ForeColor = Color.FromArgb(86, 92, 103);
        _colorManagerNote.TextAlign = ContentAlignment.MiddleLeft;
        _btnCloseColorManager.Text = "返回";
        _btnCloseColorManager.UseVisualStyleBackColor = true;
        _btnCloseColorManager.Click += (_, _) => CloseColorManager();

        ConfigureRuleGroup(_wantedGroup, _wantedTitle, _wantedSwatches);
        ConfigureRuleGroup(_excludedGroup, _excludedTitle, _excludedSwatches);
        _wantedGroup.Controls.Add(_btnPickWanted);
        _wantedGroup.Controls.Add(_btnSelectAllWanted);
        _wantedGroup.Controls.Add(_btnClearWanted);
        _excludedGroup.Controls.Add(_btnPickExcluded);
        _btnPickWanted.Text = "进入选色模式";
        _btnPickWanted.UseVisualStyleBackColor = true;
        _btnPickWanted.Click += (_, _) => EnterColorPicking(ColorPickTarget.Wanted);
        _btnSelectAllWanted.Text = "全部颜色";
        _btnSelectAllWanted.UseVisualStyleBackColor = true;
        _btnSelectAllWanted.Click += (_, _) =>
        {
            _colorRules.SelectAllWanted();
            Logger.Debug($"[color_rules] selected all {_colorRules.PaletteCount} built-in colors; excluded cleared");
            RefreshColorRuleUi();
        };
        _btnClearWanted.Text = "清空";
        _btnClearWanted.UseVisualStyleBackColor = true;
        _btnClearWanted.Click += (_, _) =>
        {
            int clearedCount = _colorRules.GetWanted().Count;
            _colorRules.ClearWanted();
            Logger.Debug($"[color_rules] cleared {clearedCount} wanted colors; excluded unchanged");
            RefreshColorRuleUi();
        };
        _btnPickExcluded.Text = "进入选色模式";
        _btnPickExcluded.UseVisualStyleBackColor = true;
        _btnPickExcluded.Click += (_, _) => EnterColorPicking(ColorPickTarget.Excluded);

        _colorPickPage.Visible = false;
        _colorPickPage.BackColor = _colorManagerOverlay.BackColor;
        _colorPickPage.Controls.Add(_pickTitle);
        _colorPickPage.Controls.Add(_pickCount);
        _colorPickPage.Controls.Add(_pickSwatches);
        _colorPickPage.Controls.Add(_pickAnimation);
        _colorPickPage.Controls.Add(_pickHint);
        _colorPickPage.Controls.Add(_btnStopColorPicking);
        _pickTitle.AutoSize = false;
        _pickTitle.Font = new Font(Font, FontStyle.Bold);
        _pickTitle.TextAlign = ContentAlignment.MiddleLeft;
        _pickCount.AutoSize = false;
        _pickCount.ForeColor = Color.FromArgb(86, 92, 103);
        _pickCount.TextAlign = ContentAlignment.MiddleLeft;
        ConfigureSwatchPanel(_pickSwatches);
        _pickHint.AutoSize = false;
        _pickHint.Text = "将鼠标移到画布颜色上，然后按下 A 键";
        _pickHint.TextAlign = ContentAlignment.MiddleCenter;
        _btnStopColorPicking.Text = "停止选色";
        _btnStopColorPicking.UseVisualStyleBackColor = true;
        _btnStopColorPicking.Click += (_, _) => ExitColorPicking(true);

        RefreshColorRuleUi();
    }

    private static void ConfigureRuleGroup(Panel group, Label title, FlowLayoutPanel swatches)
    {
        group.BackColor = Color.White;
        group.BorderStyle = BorderStyle.FixedSingle;
        group.Controls.Add(title);
        group.Controls.Add(swatches);
        title.AutoSize = false;
        title.Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold);
        title.TextAlign = ContentAlignment.MiddleLeft;
        ConfigureSwatchPanel(swatches);
    }

    private static void ConfigureSwatchPanel(FlowLayoutPanel panel)
    {
        panel.AutoScroll = true;
        panel.BackColor = Color.FromArgb(250, 251, 253);
        panel.BorderStyle = BorderStyle.FixedSingle;
        panel.FlowDirection = FlowDirection.LeftToRight;
        panel.WrapContents = false;
        panel.Padding = new Padding(3, 3, 3, 1);
    }

    private void OpenColorManager()
    {
        if (IsOperationActive())
        {
            return;
        }
        _colorPickTarget = ColorPickTarget.None;
        _colorPickPage.Visible = false;
        _colorManagerHome.Visible = true;
        _pickAnimation.StopAnimation();
        RefreshColorRuleUi();
        _colorManagerOverlay.Bounds = ClientRectangle;
        _colorManagerOverlay.Visible = true;
        _colorManagerOpen = true;
        _colorManagerOverlay.BringToFront();
        LayoutColorManagementUi();
        _btnCloseColorManager.Focus();
    }

    private void CloseColorManager()
    {
        ExitColorPicking(false);
        _colorManagerOpen = false;
        _colorManagerOverlay.Visible = false;
        _colorManagerButton.Focus();
    }

    private void EnterColorPicking(ColorPickTarget target)
    {
        _colorPickTarget = target;
        HidePreviewOverlay();
        _colorManagerHome.Visible = false;
        _colorPickPage.Visible = true;
        RefreshColorRuleUi();
        LayoutColorManagementUi();
        _pickAnimation.StartAnimation();
        _btnStopColorPicking.Focus();
        Logger.Debug($"[color_rules] entered pick mode target={target}");
    }

    private void ExitColorPicking(bool returnToManager)
    {
        if (_colorPickTarget != ColorPickTarget.None)
        {
            Logger.Debug($"[color_rules] stopped pick mode target={_colorPickTarget}");
        }
        _colorPickTarget = ColorPickTarget.None;
        Interlocked.Exchange(ref _colorPickPending, 0);
        _pickAnimation.StopAnimation();
        _colorPickPage.Visible = false;
        _colorManagerHome.Visible = returnToManager;
        if (returnToManager)
        {
            RefreshColorRuleUi();
            _btnCloseColorManager.Focus();
        }
        RestorePreviewOverlayIfChecked();
    }

    /// <summary>
    /// 从全局键盘钩子线程排队一次纯取色。返回 true 表示 A 键已属于颜色管理，应被吞掉。
    /// </summary>
    private bool QueueColorRulePick(Point triggerPosition, long triggeredAtMs)
    {
        if (!IsColorRulePicking)
        {
            return false;
        }
        if (Interlocked.CompareExchange(ref _colorPickPending, 1, 0) != 0)
        {
            return true;
        }
        BeginInvoke((Action)(() =>
        {
            try
            {
                if (!IsColorRulePicking)
                {
                    return;
                }
                var currentPosition = Cursor.Position;
                var dispatchDelay = Math.Max(0, Environment.TickCount64 - triggeredAtMs);
                Logger.Debug($"[color_rules] handling A trigger=({triggerPosition.X},{triggerPosition.Y}) current=({currentPosition.X},{currentPosition.Y}) dispatchDelayMs={dispatchDelay}");
                if (IsCursorOverSelf(triggerPosition))
                {
                    Logger.Debug($"[color_rules] A ignored: trigger position over self at ({triggerPosition.X},{triggerPosition.Y})");
                    return;
                }
                RecordColorRuleFromCursor(triggerPosition);
            }
            finally
            {
                Interlocked.Exchange(ref _colorPickPending, 0);
            }
        }));
        return true;
    }

    private void RecordColorRuleFromCursor(Point triggerPosition)
    {
        var samples = CaptureColorSamples(triggerPosition);
        var normalized = NormalizePaletteColor(samples.clear);
        if (!samples.stable || normalized.diff > ClearSamplePaletteTolerance)
        {
            _pickCount.ForeColor = Color.Firebrick;
            _pickCount.Text = $"未确认蒙版已清除（等待 {samples.waitMs}ms，色差 {normalized.diff}），本次未加入。请停稳鼠标后重试。";
            Logger.Debug($"[color_rules] rejected unstable sample trigger=({triggerPosition.X},{triggerPosition.Y}) stable={samples.stable} waitMs={samples.waitMs} reads={samples.reads} changed={samples.changedFromHover} paletteDiff={normalized.diff}");
            return;
        }
        if (_colorPickTarget == ColorPickTarget.Wanted)
        {
            _colorRules.AddWanted(normalized.color);
        }
        else if (_colorPickTarget == ColorPickTarget.Excluded)
        {
            _colorRules.AddExcluded(normalized.color);
        }
        else
        {
            return;
        }
        Logger.Debug($"[color_rules] picked target={_colorPickTarget} raw={FormatBgr(samples.clear)} normalized={FormatBgr(normalized.color)} diff={normalized.diff}");
        RefreshColorRuleUi();
    }

    private (BgrColor color, int diff) NormalizePaletteColor(BgrColor sample)
    {
        var palette = GetPredefinedColors();
        var best = palette[0];
        int minDiff = int.MaxValue;
        foreach (var color in palette)
        {
            int diff = sample.MaxDiff(color);
            if (diff < minDiff)
            {
                minDiff = diff;
                best = color;
            }
        }
        return (best, minDiff);
    }

    private void RefreshColorRuleUi()
    {
        var wanted = _colorRules.GetWanted();
        var excluded = _colorRules.GetExcluded();
        var effective = _colorRules.GetEffectiveColors();

        _wantedTitle.Text = wanted.Count == 0
            ? "我想填的颜色（未指定，默认全部）"
            : $"我想填的颜色（{wanted.Count}）";
        _excludedTitle.Text = $"我不想填的颜色（{excluded.Count}）";
        RefreshSwatches(
            _wantedSwatches,
            wanted,
            color =>
            {
                _colorRules.RemoveWanted(color);
                RefreshColorRuleUi();
            },
            "未指定，运行时默认使用全部内置颜色");
        RefreshSwatches(
            _excludedSwatches,
            excluded,
            color =>
            {
                _colorRules.RemoveExcluded(color);
                RefreshColorRuleUi();
            },
            "尚未排除颜色");

        var preview = wanted.Count > 0 ? wanted : effective;
        string summary;
        if (_colorRules.IsExplicitlyAllWanted())
        {
            summary = $"全部 {_colorRules.PaletteCount} 色";
        }
        else if (wanted.Count == 0)
        {
            summary = excluded.Count == 0 ? "默认全部颜色" : $"默认全部 · 排除 {excluded.Count}";
        }
        else
        {
            summary = excluded.Count == 0 ? $"想填 {wanted.Count} 色" : $"想填 {wanted.Count} · 排除 {excluded.Count}";
        }
        _colorManagerButton.SetSummary(preview.Take(2), summary);

        if (_colorPickTarget != ColorPickTarget.None)
        {
            bool wantedTarget = _colorPickTarget == ColorPickTarget.Wanted;
            var selected = wantedTarget ? wanted : excluded;
            _pickTitle.Text = wantedTarget ? "正在选择：我想填的颜色" : "正在选择：我不想填的颜色";
            _pickCount.ForeColor = Color.FromArgb(86, 92, 103);
            _pickCount.Text = $"已选择 {selected.Count} 色；重复选择不会增加，选择另一类会自动移动。";
            RefreshSwatches(
                _pickSwatches,
                selected,
                color =>
                {
                    if (wantedTarget)
                    {
                        _colorRules.RemoveWanted(color);
                    }
                    else
                    {
                        _colorRules.RemoveExcluded(color);
                    }
                    RefreshColorRuleUi();
                },
                "还没有选择颜色，请把鼠标移到画布上并按 A");
        }
    }

    private void RefreshSwatches(
        FlowLayoutPanel panel,
        IReadOnlyList<BgrColor> colors,
        Action<BgrColor> remove,
        string emptyText)
    {
        panel.SuspendLayout();
        try
        {
            while (panel.Controls.Count > 0)
            {
                var control = panel.Controls[0];
                panel.Controls.RemoveAt(0);
                control.Dispose();
            }
            if (colors.Count == 0)
            {
                panel.Controls.Add(new Label
                {
                    AutoSize = true,
                    Text = emptyText,
                    ForeColor = Color.FromArgb(100, 106, 116),
                    Margin = new Padding(4, 6, 4, 0)
                });
                return;
            }

            foreach (var color in colors)
            {
                var captured = color;
                var rgb = color.ToColor();
                int luminance = (rgb.R * 299 + rgb.G * 587 + rgb.B * 114) / 1000;
                int chipSize = (int)Math.Round(29 * DeviceDpi / (double)LayoutDesignDpi, MidpointRounding.AwayFromZero);
                int chipMargin = Math.Max(1, (int)Math.Round(2 * DeviceDpi / (double)LayoutDesignDpi, MidpointRounding.AwayFromZero));
                var chip = new Button
                {
                    BackColor = rgb,
                    ForeColor = luminance >= 145 ? Color.Black : Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Text = "×",
                    Font = new Font(Font.FontFamily, Math.Max(8f, Font.Size - 1f), FontStyle.Bold),
                    Size = new Size(chipSize, chipSize),
                    Margin = new Padding(chipMargin),
                    TabStop = false,
                    UseVisualStyleBackColor = false,
                    AccessibleName = $"RGB {color.R}, {color.G}, {color.B}，点击移除"
                };
                chip.FlatAppearance.BorderColor = Color.FromArgb(115, 0, 0, 0);
                chip.Click += (_, _) => remove(captured);
                _colorToolTip.SetToolTip(chip, $"RGB {color.R}, {color.G}, {color.B}（点击移除）");
                panel.Controls.Add(chip);
            }
        }
        finally
        {
            panel.ResumeLayout();
        }
    }

    private void LayoutColorManagementUi()
    {
        int S(int value) => (int)Math.Round(
            value * DeviceDpi / (double)LayoutDesignDpi,
            MidpointRounding.AwayFromZero);

        if (_colorManagerOverlay.Parent == null)
        {
            return;
        }

        _colorManagerOverlay.Bounds = ClientRectangle;
        _colorManagerHome.Bounds = _colorManagerOverlay.ClientRectangle;
        _colorPickPage.Bounds = _colorManagerOverlay.ClientRectangle;
        int width = _colorManagerOverlay.ClientSize.Width;
        int height = _colorManagerOverlay.ClientSize.Height;
        int margin = S(12);
        int gap = S(10);
        int headerHeight = S(38);
        int footerHeight = S(28);

        _colorManagerTitle.Bounds = new Rectangle(margin, S(8), Math.Max(S(120), width - S(190)), S(28));
        _btnCloseColorManager.Bounds = new Rectangle(Math.Max(margin, width - margin - S(72)), S(8), S(72), S(28));
        _colorManagerNote.Bounds = new Rectangle(margin, Math.Max(0, height - footerHeight - S(2)), Math.Max(0, width - margin * 2), footerHeight);

        if (width >= S(600))
        {
            int groupY = headerHeight + S(4);
            int groupHeight = Math.Max(S(96), height - groupY - footerHeight - gap);
            int groupWidth = Math.Max(S(180), (width - margin * 2 - gap) / 2);
            _wantedGroup.Bounds = new Rectangle(margin, groupY, groupWidth, groupHeight);
            _excludedGroup.Bounds = new Rectangle(margin + groupWidth + gap, groupY, groupWidth, groupHeight);
        }
        else
        {
            int groupY = headerHeight + S(4);
            int available = Math.Max(S(210), height - groupY - footerHeight - gap * 2);
            int groupHeight = Math.Min(S(132), Math.Max(S(112), available / 2));
            _wantedGroup.Bounds = new Rectangle(margin, groupY, Math.Max(S(220), width - margin * 2), groupHeight);
            _excludedGroup.Bounds = new Rectangle(margin, groupY + groupHeight + gap, Math.Max(S(220), width - margin * 2), groupHeight);
            _colorManagerNote.Bounds = new Rectangle(
                margin,
                _excludedGroup.Bottom + S(4),
                Math.Max(0, width - margin * 2),
                footerHeight);
        }
        LayoutRuleGroup(_wantedGroup, _wantedTitle, _wantedSwatches, _btnPickWanted, _btnSelectAllWanted, _btnClearWanted);
        LayoutRuleGroup(_excludedGroup, _excludedTitle, _excludedSwatches, _btnPickExcluded, null, null);

        if (width >= S(600))
        {
            _pickTitle.Bounds = new Rectangle(margin, S(10), S(285), S(28));
            _pickCount.Bounds = new Rectangle(margin, S(38), S(285), S(38));
            _pickSwatches.Bounds = new Rectangle(margin, S(80), S(285), Math.Max(S(42), height - S(94)));
            int animationSize = Math.Max(S(88), Math.Min(S(132), height - S(28)));
            _pickAnimation.Bounds = new Rectangle(S(315), Math.Max(S(8), (height - animationSize) / 2), animationSize, animationSize);
            _pickHint.Bounds = new Rectangle(S(445), Math.Max(S(28), height / 2 - S(30)), Math.Max(S(130), width - S(457)), S(42));
            _btnStopColorPicking.Bounds = new Rectangle(Math.Max(S(445), width - margin - S(126)), Math.Max(S(76), height / 2 + S(20)), S(126), S(32));
        }
        else
        {
            _pickTitle.Bounds = new Rectangle(margin, S(10), Math.Max(S(220), width - margin * 2), S(28));
            _pickCount.Bounds = new Rectangle(margin, S(40), Math.Max(S(220), width - margin * 2), S(38));
            _pickSwatches.Bounds = new Rectangle(margin, S(80), Math.Max(S(220), width - margin * 2), S(46));
            int animationSize = Math.Max(S(96), Math.Min(S(158), height - S(250)));
            _pickAnimation.Bounds = new Rectangle((width - animationSize) / 2, S(135), animationSize, animationSize);
            _pickHint.Bounds = new Rectangle(margin, S(140) + animationSize, Math.Max(S(220), width - margin * 2), S(42));
            _btnStopColorPicking.Bounds = new Rectangle((width - S(140)) / 2, Math.Max(S(330), height - S(55)), S(140), S(34));
        }
    }

    private static void LayoutRuleGroup(
        Panel group,
        Label title,
        FlowLayoutPanel swatches,
        Button primary,
        Button? secondary,
        Button? tertiary)
    {
        int S(int value) => (int)Math.Round(
            value * group.DeviceDpi / (double)LayoutDesignDpi,
            MidpointRounding.AwayFromZero);
        int width = group.ClientSize.Width;
        int height = group.ClientSize.Height;
        title.Bounds = new Rectangle(S(10), S(6), Math.Max(S(120), width - S(20)), S(24));
        int buttonY = Math.Max(S(64), height - S(36));
        swatches.Bounds = new Rectangle(S(10), S(32), Math.Max(S(120), width - S(20)), Math.Max(S(30), buttonY - S(38)));
        primary.Bounds = new Rectangle(S(10), buttonY, S(124), S(27));
        if (secondary != null)
        {
            secondary.Bounds = new Rectangle(S(142), buttonY, S(88), S(27));
        }
        if (tertiary != null)
        {
            tertiary.Bounds = new Rectangle(S(238), buttonY, S(64), S(27));
        }
    }
}
}
