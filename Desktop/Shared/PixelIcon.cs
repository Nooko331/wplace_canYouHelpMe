using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Wplace.Desktop.Controls;

/// <summary>Small editable pixel glyphs; no font glyph or external image dependency.</summary>
public sealed class PixelIcon : Control
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<PixelIcon, string>(nameof(Kind), "play");
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<PixelIcon>();
    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    static PixelIcon() => AffectsRender<PixelIcon>(KindProperty, ForegroundProperty);
    public PixelIcon()
    {
        Width = Height = 24;
        IsHitTestVisible = false;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }
    public override void Render(DrawingContext context)
    {
        var unit = Math.Min(Bounds.Width, Bounds.Height) / 16;
        var ox = (Bounds.Width - unit * 16) / 2;
        var oy = (Bounds.Height - unit * 16) / 2;
        var ink = Foreground ?? Brush.Parse("#27486F");
        void R(double x, double y, double w, double h, IBrush? brush = null) =>
            context.FillRectangle(brush ?? ink, new Rect(ox + x * unit, oy + y * unit, w * unit, h * unit));
        void Pixels(string[] rows, IBrush? brush = null)
        {
            for (var y = 0; y < rows.Length; y++)
                for (var x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] != '.') R(x, y, 1, 1, brush);
        }
        switch (Kind)
        {
            case "pen":
                for (var i = 0; i < 9; i++) R(4 + i, 11 - i, 4, 4);
                for (var i = 0; i < 8; i++) R(5 + i, 11 - i, 2, 2, Brush.Parse("#086BFA"));
                R(2,13,4,2); R(2,11,2,4); break;
            case "cpu":
                R(3,3,10,10); R(5,5,6,6, Brushes.White); R(7,7,2,2);
                for(var i=4;i<13;i+=4){R(i,0,2,3);R(i,13,2,3);R(0,i,3,2);R(13,i,3,2);} break;
            case "notes":
                R(3,1,10,14); R(5,3,6,10,Brushes.White); R(6,5,4,1); R(6,8,4,1); R(6,11,4,1); break;
            case "book":
                R(1,2,6,12); R(9,2,6,12); R(7,4,2,11); R(3,4,3,8,Brushes.White); R(10,4,3,8,Brushes.White); break;
            case "info":
                R(7,2,2,2); R(6,6,3,7); R(5,13,6,1); break;
            case "compact":
                R(1,2,14,2); R(1,6,14,8); R(3,8,3,4,Brushes.White); R(8,8,5,4,Brushes.White); break;
            case "grip":
                for (var y = 3; y <= 11; y += 4) { R(3, y, 2, 2); R(9, y, 2, 2); }
                break;
            case "play":
                for (var y = 1; y <= 14; y++) R(4, y, Math.Min(y, 15-y), 1);
                break;
            case "eye":
                Pixels(["................","................",".....######.....","...###....###...","..##........##..",".##...####...##.","##...######...##",".##..######..##.","..##..####..##..","...##......##...","....########....","................","................","................","................","................"]);
                break;
            case "range":
                R(1,1,4,2); R(7,1,3,2); R(12,1,3,2); R(1,1,2,4); R(1,7,2,3); R(1,12,2,3);
                R(13,4,2,3); R(13,9,2,3); R(1,13,4,2); R(7,13,3,2); R(12,13,3,2);
                R(11,0,5,5,new SolidColorBrush(Color.Parse("#17365B"))); R(12,1,3,3,new SolidColorBrush(Color.Parse("#53C987")));
                break;
            case "palette":
                Pixels([".....######.....","...###....###...","..##........##..",".##..........##.",".#............#.","##............##","#..............#","#..............#","#..............#","##.........#####",".#.......###....",".##......#......","..##.....#......","...####..###....","......######....","................"]);
                R(5,3,3,3,new SolidColorBrush(Color.Parse("#FFCB32")));
                R(10,3,3,3,new SolidColorBrush(Color.Parse("#F04B4D")));
                R(2,7,3,3,new SolidColorBrush(Color.Parse("#48B96E")));
                R(10,7,3,3,new SolidColorBrush(Color.Parse("#006BFF")));
                break;
            case "step":
                R(6,1,5,1); R(6,2,1,4); R(10,2,1,4); R(6,5,5,1);
                R(1,9,1,6); R(14,9,1,6); R(2,11,12,2);
                R(3,10,1,4); R(4,9,1,6); R(12,10,1,4); R(11,9,1,6);
                break;
            case "gauge":
                Pixels(["................",".....######.....","...###....###...","..##........##..",".##..........##.",".#............#.","##............##","#..............#","#......##......#","#.....###......#","#....####......#","##..###.......##",".##..........##.","..############..","................","................"]);
                R(9,4,2,2); R(8,6,2,2); R(7,8,2,2); break;
            case "bolt":
                Pixels([".........###....","........###.....",".......###......","......###.......",".....###........","....###.........","...##########...","...#########....",".......####.....","......####......",".....####.......","....####........","...####.........","..####..........","..###...........","................"]);
                break;
            case "missing":
                R(8,1,3,1); R(13,1,2,1); R(15,1,1,3); R(15,6,1,3); R(12,9,4,1); R(8,4,1,2);
                R(2,5,4,1); R(1,6,1,5); R(0,7,1,3); R(6,6,1,5); R(7,7,1,3); R(2,11,4,1);
                R(6,11,2,2); R(8,13,2,2); R(10,14,2,2); break;
            case "clock":
                Pixels(["......####......",".......##.......",".....######.....","...###....###...","..##........##..",".##..........##.",".#.....##.....#.","##.....##.....##","##.....##.....##","##.....#####..##",".#............#.",".##..........##.","..##........##..","...###....###...",".....######.....","................"]);
                break;
            case "expand":
                R(9,1,6,2); R(13,1,2,6); R(10,4,3,2); R(8,6,3,2);
                R(1,9,2,6); R(1,13,6,2); R(4,10,2,3); R(6,8,2,3); break;
            case "minimize": R(3,8,10,2); break;
            case "close":
                for(var i=3;i<13;i++) { R(i,i,1,2); R(15-i,i,1,2); } break;
            case "up": R(7,5,2,1); R(6,6,4,1); R(5,7,6,1); R(4,8,8,2); break;
            case "down": R(4,5,8,2); R(5,7,6,1); R(6,8,4,1); R(7,9,2,1); break;
            case "back": R(2,7,12,2); R(3,5,2,2); R(3,9,2,2); R(5,3,2,2); R(5,11,2,2); break;
        }
    }
}
