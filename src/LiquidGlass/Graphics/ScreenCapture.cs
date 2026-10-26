using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiquidGlass.Interop;

namespace LiquidGlass.Graphics;

/// <summary>Capturas GDI em pixels BGRA opacos (4 bytes por pixel, linhas contíguas).</summary>
internal static class ScreenCapture
{
    private const uint SourceCopy = 0x00CC0020;

    /// <summary>Papel de parede como o Windows o desenha, no tamanho informado (em pixels físicos).</summary>
    public static byte[] Wallpaper(int width, int height)
    {
        return Capture(width, height, memoryDc => User32.PaintDesktop(memoryDc));
    }

    /// <summary>
    /// O que está na tela agora dentro de <paramref name="region"/> (em pixels físicos), reduzido
    /// <paramref name="factor"/> vezes em cada eixo. O próprio GDI tira a média dos pixels ao ler a
    /// tela, então a resolução total nunca chega à memória gerenciada.
    /// </summary>
    public static byte[] Screen(Int32Rect region, int factor, out int width, out int height)
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

        var screenDc = User32.GetDC(0);
        var memoryDc = Gdi32.CreateCompatibleDC(screenDc);
        var bitmap = Gdi32.CreateDIBSection(screenDc, ref header, 0, out var bits, 0, 0);
        var previous = Gdi32.SelectObject(memoryDc, bitmap);

        try
        {
            Gdi32.SetStretchBltMode(memoryDc, Gdi32.Halftone);
            Gdi32.SetBrushOrgEx(memoryDc, 0, 0, 0);
            Gdi32.StretchBlt(
                memoryDc, 0, 0, width, height, screenDc, region.X, region.Y, width * factor, height * factor, SourceCopy);
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
            User32.ReleaseDC(0, screenDc);
        }
    }

    private static byte[] Capture(int width, int height, Action<nint> draw)
    {
        var screenDc = User32.GetDC(0);
        var memoryDc = Gdi32.CreateCompatibleDC(screenDc);
        var bitmap = Gdi32.CreateCompatibleBitmap(screenDc, width, height);
        var previous = Gdi32.SelectObject(memoryDc, bitmap);

        try
        {
            draw(memoryDc);
            Gdi32.SelectObject(memoryDc, previous);

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);
            MakeOpaque(pixels);
            return pixels;
        }
        finally
        {
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(memoryDc);
            User32.ReleaseDC(0, screenDc);
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
