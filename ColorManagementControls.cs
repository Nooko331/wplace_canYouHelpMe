using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WplaceColorWatch
{

/// <summary>
/// 主界面的颜色管理入口：只预览两个色块，避免在主界面展开完整色表。
/// </summary>
public sealed class ColorManagerButton : Control
{
    private readonly List<BgrColor> _previewColors = new();
    private string _summary = "默认全部颜色";
    private bool _hovered;

    public ColorManagerButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable,
            true);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "颜色管理";
    }

    public void SetSummary(IEnumerable<BgrColor> previewColors, string summary)
    {
        _previewColors.Clear();
        foreach (var color in previewColors)
        {
            if (_previewColors.Count >= 2)
            {
                break;
            }
            _previewColors.Add(color);
        }
        _summary = summary;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int S(int value) => (int)Math.Round(value * DeviceDpi / 96d, MidpointRounding.AwayFromZero);
        var back = Enabled
            ? (_hovered ? Color.FromArgb(240, 247, 255) : Color.White)
            : SystemColors.Control;
        using (var background = new SolidBrush(back))
        {
            g.FillRectangle(background, ClientRectangle);
        }
        using (var border = new Pen(Focused ? Color.FromArgb(55, 115, 205) : Color.FromArgb(165, 172, 182), Focused ? Math.Max(2f, S(2)) : Math.Max(1f, S(1))))
        {
            g.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }

        var titleRect = new Rectangle(S(10), S(3), Math.Max(0, Width - S(150)), Math.Max(0, Height / 2));
        TextRenderer.DrawText(g, "颜色管理", Font, titleRect, Enabled ? ForeColor : SystemColors.GrayText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        var summaryRect = new Rectangle(S(10), Math.Max(S(14), Height / 2 - S(1)), Math.Max(0, Width - S(150)), Math.Max(0, Height / 2));
        using (var smallFont = new Font(Font.FontFamily, Math.Max(7f, Font.Size - 1f), FontStyle.Regular))
        {
            TextRenderer.DrawText(g, _summary, smallFont, summaryRect, Enabled ? Color.FromArgb(82, 88, 98) : SystemColors.GrayText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        int swatchSize = Math.Max(S(16), Math.Min(S(24), Height - S(12)));
        int right = Width - S(31);
        for (int i = _previewColors.Count - 1; i >= 0; i--)
        {
            var rect = new Rectangle(right - swatchSize, (Height - swatchSize) / 2, swatchSize, swatchSize);
            using (var brush = new SolidBrush(_previewColors[i].ToColor()))
            {
                g.FillRectangle(brush, rect);
            }
            using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0)))
            {
                g.DrawRectangle(pen, rect);
            }
            right -= swatchSize + S(6);
        }
        using (var arrowFont = new Font(Font.FontFamily, Font.Size + 4f, FontStyle.Regular))
        {
            TextRenderer.DrawText(g, "›", arrowFont,
                new Rectangle(Width - S(24), 0, S(20), Height), Enabled ? ForeColor : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>
/// 选色模式中央的脉冲 A 键提示。
/// </summary>
public sealed class AnimatedKeyHint : Control
{
    private readonly Timer _timer;
    private double _phase;

    public AnimatedKeyHint()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        _timer = new Timer { Interval = 40 };
        _timer.Tick += (_, _) =>
        {
            _phase += 0.16;
            Invalidate();
        };
    }

    public void StartAnimation()
    {
        _timer.Start();
    }

    public void StopAnimation()
    {
        _timer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int baseSize = Math.Max(34, Math.Min(Width, Height) - 18);
        float pulse = (float)(1.0 + Math.Sin(_phase) * 0.055);
        int size = Math.Max(30, (int)(baseSize * pulse));
        var rect = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);

        int haloAlpha = 34 + (int)((Math.Sin(_phase) + 1d) * 18d);
        using (var halo = new SolidBrush(Color.FromArgb(haloAlpha, 56, 132, 255)))
        {
            e.Graphics.FillEllipse(halo, rect);
        }

        int inset = Math.Max(7, size / 9);
        var keyRect = Rectangle.Inflate(rect, -inset, -inset);
        using (var keyBrush = new SolidBrush(Color.FromArgb(50, 116, 224)))
        using (var keyPen = new Pen(Color.FromArgb(31, 82, 164), 2f))
        {
            e.Graphics.FillRectangle(keyBrush, keyRect);
            e.Graphics.DrawRectangle(keyPen, keyRect);
        }
        using (var font = new Font(Font.FontFamily, Math.Max(18f, keyRect.Height * 0.48f), FontStyle.Bold, GraphicsUnit.Pixel))
        {
            TextRenderer.DrawText(e.Graphics, "A", font, keyRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
}
