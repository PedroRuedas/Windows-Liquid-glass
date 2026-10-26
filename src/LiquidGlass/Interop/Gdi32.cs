using System.Runtime.InteropServices;

namespace LiquidGlass.Interop;

internal static class Gdi32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DibSection
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPerPixel;
        public nint Bits;
        public uint HeaderSize;
        public int HeaderWidth;
        public int HeaderHeight;
        public ushort HeaderPlanes;
        public ushort HeaderBitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
        public uint BitField0;
        public uint BitField1;
        public uint BitField2;
        public nint Section;
        public uint Offset;
    }

    [DllImport("gdi32.dll")]
    public static extern int GetObject(nint gdiObject, int size, out DibSection section);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(nint hdc);
}
