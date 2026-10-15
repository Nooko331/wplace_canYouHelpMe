using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Wplace.ToolbarPrototype;

/// <summary>Selection interaction restricted to this prototype's own canvas.</summary>
public sealed class RangePreview : Control
{
    private readonly ToolbarState state;
    private Point? origin;
    private Rect selection = new(90, 55, 380, 180);
    public event EventHandler<Rect>? RangeAccepted;
    public RangePreview(ToolbarState state)
    {
        this.state = state;
        ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Cross);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            origin = Clamp(e.GetPosition(this));
            selection = new Rect(origin.Value, origin.Value);
            e.Pointer.Capture(this); InvalidateVisual();
        };
        PointerMoved += (_, e) =>
        {
            if (origin is null) return;
            selection = new Rect(origin.Value, Clamp(e.GetPosition(this))).Normalize();
            InvalidateVisual();
        };
        PointerReleased += (_, e) =>
        {
            if (origin is null) return;
            origin = null; e.Pointer.Capture(null);
            if (selection.Width >= 8 && selection.Height >= 8) RangeAccepted?.Invoke(this, selection);
            else InvalidateVisual();
        };
        PointerCaptureLost += (_, _) => { origin = null; };
    }
    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, Bounds.Width), Math.Clamp(p.Y, 0, Bounds.Height));
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        IBrush[] colors = [Brush.Parse("#85A8CE"), Brush.Parse("#799BC6"), Brush.Parse("#70A092"), Brush.Parse("#A3B89B"), Brush.Parse("#B7BAAD")];
        const int cell = 20;
        for (var y = 0; y < Bounds.Height; y += cell)
            for (var x = 0; x < Bounds.Width; x += cell)
            {
                var wave = Math.Sin(x / 75d) * 50 + y * .4;
                var index = x < 170 + wave ? 2 + (x / cell + y / cell) % 2 : x < 230 + wave ? 4 : (x / cell + y / cell) % 2;
                context.FillRectangle(colors[index], new Rect(x, y, cell - 1, cell - 1));
            }
        var shade = new SolidColorBrush(Color.FromArgb(110, 20, 41, 68));
        context.FillRectangle(shade, new Rect(0, 0, Bounds.Width, selection.Y));
        context.FillRectangle(shade, new Rect(0, selection.Bottom, Bounds.Width, Math.Max(0, Bounds.Height - selection.Bottom)));
        context.FillRectangle(shade, new Rect(0, selection.Y, selection.X, selection.Height));
        context.FillRectangle(shade, new Rect(selection.Right, selection.Y, Math.Max(0, Bounds.Width - selection.Right), selection.Height));
        context.DrawRectangle(null, new Pen(Brushes.White, 5), selection);
        context.DrawRectangle(null, new Pen(Brush.Parse("#F1283C"), 2), selection);
        if (state.ShowRange)
        {
            var spacing = Math.Max(8, state.Step * 2);
            for (var y = selection.Y + spacing / 2d; y < selection.Bottom - 2; y += spacing)
                for (var x = selection.X + spacing / 2d; x < selection.Right - 2; x += spacing)
                {
                    context.FillRectangle(Brush.Parse("#273E59"), new Rect(x - 2, y - 2, 4, 4));
                    context.FillRectangle(Brush.Parse("#FF2C3D"), new Rect(x - 1, y - 1, 2, 2));
                }
        }
    }
}
