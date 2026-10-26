using System.Runtime.InteropServices;
using System.Windows;
using LiquidGlass.Interop;

namespace LiquidGlass.Graphics;

/// <summary>
/// Capturas GDI em pixels BGRA opacos (4 bytes por pixel, linhas contíguas), reduzidas
/// <c>factor</c> vezes em cada eixo. O próprio GDI tira a média dos pixels ao reduzir, então a
/// resolução total nunca chega à memória gerenciada.
/// </summary>
internal static class ScreenCapture
{
    private const uint SourceCopy = 0x00CC0020;

    /// <summary>Papel de parede como o Windows o desenha, para um monitor do tamanho informado (em pixels físicos).</summary>
    public static byte[] Wallpaper(int monitorWidth, int monitorHeight, int factor, out int width, out int height)
    {
        var screenDc = User32.GetDC(0);
        var paintDc = Gdi32.CreateCompatibleDC(screenDc);
        var paintBitmap = Gdi32.CreateCompatibleBitmap(screenDc, monitorWidth, monitorHeight);
        var previous = Gdi32.SelectObject(paintDc, paintBitmap);

        try
        {
            User32.PaintDesktop(paintDc);
            return Downsample(paintDc, new Int32Rect(0, 0, monitorWidth, monitorHeight), factor, out width, out height);
        }
        finally
        {
            Gdi32.SelectObject(paintDc, previous);
            Gdi32.DeleteObject(paintBitmap);
            Gdi32.DeleteDC(paintDc);
            User32.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>O que está na tela agora dentro de <paramref name="region"/> (em pixels físicos).</summary>
    public static byte[] Screen(Int32Rect region, int factor, out int width, out int height)
    {
        var screenDc = User32.GetDC(0);
        try
        {
            return Downsample(screenDc, region, factor, out width, out height);
        }
        finally
        {
            User32.ReleaseDC(0, screenDc);
        }
    }

    private static byte[] Downsample(nint sourceDc, Int32Rect region, int factor, out int width, out int height)
    {
        width = Math.Max(region.Width / factor, 1);
        height = Math.Max(region.Height / factor, 1);

        // Altura negativa: DIB de cima para baixo, na mesma ordem de linhas que o BitmapSource.
        var header = new Gdi32.BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<Gdi32.BitmapInfoHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };

        var memoryDc = Gdi32.CreateCompatibleDC(sourceDc);
        var bitmap = Gdi32.CreateDIBSection(sourceDc, ref header, 0, out var bits, 0, 0);
        var previous = Gdi32.SelectObject(memoryDc, bitmap);

        try
        {
            Gdi32.SetStretchBltMode(memoryDc, Gdi32.Halftone);
            Gdi32.SetBrushOrgEx(memoryDc, 0, 0, 0);
            Gdi32.StretchBlt(
                memoryDc, 0, 0, width, height, sourceDc, region.X, region.Y, width * factor, height * factor, SourceCopy);
            Gdi32.GdiFlush();

            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            MakeOpaque(pixels);
            return pixels;
        }
        finally
        {
            Gdi32.SelectObject(memoryDc, previous);
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(memoryDc);
        }
    }

    /// <summary>Bitmaps GDI não preenchem o canal alfa; o fundo capturado é sempre opaco.</summary>
    private static void MakeOpaque(byte[] pixels)
    {
        for (var alpha = 3; alpha < pixels.Length; alpha += 4)
        {
            pixels[alpha] = 255;
        }
    }
}
