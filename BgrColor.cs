using System;
using System.Drawing;

namespace WplaceColorWatch
{

public readonly struct BgrColor
{
    public readonly byte B;
    public readonly byte G;
    public readonly byte R;

    public BgrColor(byte b, byte g, byte r)
    {
        B = b;
        G = g;
        R = r;
    }

    public static BgrColor FromColor(Color c)
    {
        return new BgrColor(c.B, c.G, c.R);
    }

    public Color ToColor()
    {
        return Color.FromArgb(R, G, B);
    }

    public int MaxDiff(BgrColor other)
    {
        int db = Math.Abs(B - other.B);
        int dg = Math.Abs(G - other.G);
        int dr = Math.Abs(R - other.R);
        return Math.Max(db, Math.Max(dg, dr));
    }

    public int[] ToRgbArray()
    {
        return new[] { (int)R, (int)G, (int)B };
    }
}
}

