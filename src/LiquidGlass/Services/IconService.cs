using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>
/// Obtém ícones em alta resolução (256 px) pelo shell, com cache, para que continuem nítidos
/// quando ampliados pela dock.
/// </summary>
public sealed class IconService
{
    private const int IconResolution = 256;

    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ícone de um arquivo, atalho, pasta ou <c>shell:AppsFolder\{AUMID}</c>.</summary>
    public ImageSource? GetIcon(string parsingName)
    {
        if (_cache.TryGetValue(parsingName, out var cached))
        {
            return cached;
        }

        var icon = LoadShellIcon(parsingName);
        _cache[parsingName] = icon;
        return icon;
    }

    /// <summary>Ícone que a própria janela declara; usado quando o executável é inacessível.</summary>
    public static ImageSource? GetWindowIcon(nint hwnd)
    {
        User32.SendMessageTimeout(hwnd, User32.WmGetIcon, User32.IconBig, 0, User32.SmtoAbortIfHung, 100, out var icon);
        if (icon == 0)
        {
            icon = User32.GetClassLongPtr(hwnd, User32.GclpHIcon);
        }

        if (icon == 0)
        {
            return null;
        }

        var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static BitmapSource? LoadShellIcon(string parsingName)
    {
        var factory = Shell32.CreateImageFactory(parsingName);
        if (factory is null)
        {
            return null;
        }

        try
        {
            var size = new NativeSize { Width = IconResolution, Height = IconResolution };
            var result = factory.GetImage(size, Shell32.SiigbfResizeToFit | Shell32.SiigbfIconOnly, out var bitmap);
            if (result != 0 || bitmap == 0)
            {
                return null;
            }

            try
            {
                return ToBitmapSource(bitmap);
            }
            finally
            {
                Gdi32.DeleteObject(bitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>
    /// Converte o HBITMAP do shell preservando a transparência (Imaging.CreateBitmapSourceFromHBitmap
    /// descarta o canal alfa).
    /// </summary>
    private static BitmapSource ToBitmapSource(nint bitmap)
    {
        var size = Marshal.SizeOf<Gdi32.DibSection>();
        if (Gdi32.GetObject(bitmap, size, out var dib) == 0 || dib.Bits == 0 || dib.BitsPerPixel != 32)
        {
            var fallback = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            fallback.Freeze();
            return fallback;
        }

        var width = dib.Width;
        var height = Math.Abs(dib.Height);
        var stride = dib.WidthBytes;
        var pixels = new byte[stride * height];
        var bottomUp = dib.HeaderHeight > 0;

        for (var row = 0; row < height; row++)
        {
            var sourceRow = bottomUp ? height - 1 - row : row;
            Marshal.Copy(dib.Bits + (sourceRow * stride), pixels, row * stride, stride);
        }

        var hasAlpha = false;
        for (var alpha = 3; alpha < pixels.Length && !hasAlpha; alpha += 4)
        {
            hasAlpha = pixels[alpha] != 0;
        }

        var format = hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
        var source = BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
        source.Freeze();
        return source;
    }
}
