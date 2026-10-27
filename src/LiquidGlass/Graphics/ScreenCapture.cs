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

    /// <summary>
    /// A área de trabalho (papel de parede e ícones) como o Explorer a desenha, sem nenhuma janela por
    /// cima. É a mesma em todas as áreas de trabalho virtuais. Cai para só o papel de parede se o
    /// Explorer não responder.
    /// </summary>
    public static byte[] Desktop(Int32Rect monitor)
    {
        var desktop = User32.FindWindow("Progman", null);
        var screen = User32.GetVirtualScreen();
        var pixels = new byte[monitor.Width * monitor.Height * 4];
        if (desktop == 0 || !PrintInto(desktop, new User32.Rect { Left = screen.Left, Top = screen.Top, Right = screen.Right, Bottom = screen.Bottom }, monitor, pixels, monitor))
        {
            return Wallpaper(monitor.Width, monitor.Height, 1, out _, out _);
        }

        return pixels;
    }

    /// <summary>
    /// Janelas desenhadas pelo próprio Windows (mesmo as escondidas em outra área de trabalho), de
    /// baixo para cima, numa camada do tamanho do monitor: transparente fora delas.
    /// </summary>
    public static byte[] PrintWindows(Int32Rect monitor, IReadOnlyList<nint> windowsBottomToTop)
    {
        var layer = new byte[monitor.Width * monitor.Height * 4];
        foreach (var hwnd in windowsBottomToTop)
        {
            if (!User32.GetWindowRect(hwnd, out var frame) || frame.Width <= 0 || frame.Height <= 0)
            {
                continue;
            }

            // Só a parte visível: sem as bordas invisíveis de redimensionamento.
            var visible = Dwmapi.GetVisibleBounds(hwnd) ?? frame;
            var area = new Int32Rect(visible.Left, visible.Top, visible.Width, visible.Height);
            PrintInto(hwnd, frame, area, layer, monitor);
        }

        return layer;
    }

    /// <summary>
    /// Só as janelas que estão na tela agora: os retângulos (relativos ao monitor) vêm da tela; o
    /// resto da camada fica transparente.
    /// </summary>
    public static byte[] ScreenWindows(Int32Rect monitor, IEnumerable<Int32Rect> windows)
    {
        var screen = Screen(monitor, 1, out _, out _);
        var layer = new byte[screen.Length];
        foreach (var rect in windows)
        {
            for (var y = rect.Y; y < rect.Y + rect.Height; y++)
            {
                var offset = ((y * monitor.Width) + rect.X) * 4;
                Buffer.BlockCopy(screen, offset, layer, offset, rect.Width * 4);
            }
        }

        return layer;
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

    /// <summary>
    /// Pede à janela que se desenhe (PrintWindow) e copia o trecho <paramref name="area"/> (em pixels da
    /// tela) para <paramref name="target"/>, um bitmap BGRA que cobre <paramref name="monitor"/>.
    /// </summary>
    private static bool PrintInto(nint hwnd, User32.Rect frame, Int32Rect area, byte[] target, Int32Rect monitor)
    {
        var left = Math.Max(area.X, Math.Max(frame.Left, monitor.X));
        var top = Math.Max(area.Y, Math.Max(frame.Top, monitor.Y));
        var right = Math.Min(area.X + area.Width, Math.Min(frame.Right, monitor.X + monitor.Width));
        var bottom = Math.Min(area.Y + area.Height, Math.Min(frame.Bottom, monitor.Y + monitor.Height));
        if (right <= left || bottom <= top)
        {
            return false;
        }

        var header = new Gdi32.BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<Gdi32.BitmapInfoHeader>(),
            Width = frame.Width,
            Height = -frame.Height,
            Planes = 1,
            BitCount = 32,
        };

        var screenDc = User32.GetDC(0);
        var memoryDc = Gdi32.CreateCompatibleDC(screenDc);
        var bitmap = Gdi32.CreateDIBSection(screenDc, ref header, 0, out var bits, 0, 0);
        var previous = Gdi32.SelectObject(memoryDc, bitmap);
        try
        {
            if (bitmap == 0 || !User32.PrintWindow(hwnd, memoryDc, User32.PwRenderFullContent))
            {
                return false;
            }

            Gdi32.GdiFlush();
            var length = (right - left) * 4;
            for (var y = top; y < bottom; y++)
            {
                var source = bits + ((((y - frame.Top) * frame.Width) + (left - frame.Left)) * 4);
                var offset = (((y - monitor.Y) * monitor.Width) + (left - monitor.X)) * 4;
                Marshal.Copy(source, target, offset, length);
                for (var alpha = offset + 3; alpha < offset + length; alpha += 4)
                {
                    target[alpha] = 255;
                }
            }

            return true;
        }
        finally
        {
            Gdi32.SelectObject(memoryDc, previous);
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
