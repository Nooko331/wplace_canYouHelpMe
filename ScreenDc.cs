using System;

namespace WplaceColorWatch
{

public sealed class ScreenDc : IDisposable
{
    private IntPtr _hdc = IntPtr.Zero;

    public ScreenDc()
    {
        _hdc = NativeMethods.GetDC(IntPtr.Zero);
    }

    public BgrColor GetPixel(int x, int y)
    {
        uint colorRef = NativeMethods.GetPixel(_hdc, x, y);
        byte r = (byte)(colorRef & 0x000000FF);
        byte g = (byte)((colorRef & 0x0000FF00) >> 8);
        byte b = (byte)((colorRef & 0x00FF0000) >> 16);
        return new BgrColor(b, g, r);
    }

    public void Dispose()
    {
        if (_hdc != IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, _hdc);
            _hdc = IntPtr.Zero;
        }
    }
}
}

