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

    /// <summary>O que está na tela agora dentro de <paramref name="region"/> (em pixels físicos).</summary>
    public static byte[] Screen(Int32Rect region)
    {
        return Capture(region.Width, region.Height, memoryDc =>
        {
            var screenDc = User32.GetDC(0);
            try
            {
                Gdi32.BitBlt(memoryDc, 0, 0, region.Width, region.Height, screenDc, region.X, region.Y, SourceCopy);
            }
            finally
            {
                User32.ReleaseDC(0, screenDc);
            }
        });
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

            // Bitmaps GDI não preenchem o canal alfa; o fundo é sempre opaco.
            for (var alpha = 3; alpha < pixels.Length; alpha += 4)
            {
                pixels[alpha] = 255;
            }

            return pixels;
        }
        finally
        {
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(memoryDc);
            User32.ReleaseDC(0, screenDc);
        }
    }
}
