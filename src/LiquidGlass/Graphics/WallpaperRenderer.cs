using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiquidGlass.Interop;

namespace LiquidGlass.Graphics;

/// <summary>
/// Desenha o papel de parede de um monitor qualquer como o Windows o mostra: a imagem desse
/// monitor, com o ajuste escolhido (preencher, ajustar, centralizar…) e a cor de fundo. O
/// PaintDesktop do GDI só acerta o monitor principal; nos outros ele repete o desenho dele.
/// </summary>
internal static class WallpaperRenderer
{
    /// <summary>Pixels BGRA do monitor (em pixels físicos), reduzidos <paramref name="factor"/> vezes; nulo se não der.</summary>
    public static byte[]? Render(User32.Rect monitor, User32.Rect virtualScreen, int factor, out int width, out int height)
    {
        width = Math.Max(monitor.Width / factor, 1);
        height = Math.Max(monitor.Height / factor, 1);

        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            var id = FindMonitor(wallpaper, monitor);
            var path = id is null ? null : wallpaper.GetWallpaper(id);
            var position = wallpaper.GetPosition();
            var background = FromColorRef(wallpaper.GetBackgroundColor());
            var image = string.IsNullOrEmpty(path) || !File.Exists(path) ? null : Load(path);

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var target = new Rect(0, 0, width, height);
                context.DrawRectangle(new SolidColorBrush(background), null, target);
                if (image is not null)
                {
                    context.PushClip(new RectangleGeometry(target));
                    Draw(context, image, position, monitor, virtualScreen, factor);
                    context.Pop();
                }
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or IOException or NotSupportedException or UnauthorizedAccessException or FileFormatException)
        {
            return null;
        }
        finally
        {
            if (wallpaper is not null)
            {
                Marshal.ReleaseComObject(wallpaper);
            }
        }
    }

    /// <summary>
    /// Onde a imagem fica no monitor, em pixels físicos relativos ao canto dele, para cada ajuste.
    /// "Estender" cobre a área de todos os monitores, e cada um mostra o seu pedaço.
    /// </summary>
    public static Rect Place(WallpaperPosition position, Size image, User32.Rect monitor, User32.Rect virtualScreen)
    {
        var (w, h) = ((double)monitor.Width, (double)monitor.Height);
        switch (position)
        {
            case WallpaperPosition.Stretch:
                return new Rect(0, 0, w, h);
            case WallpaperPosition.Fit:
                {
                    var scale = Math.Min(w / image.Width, h / image.Height);
                    return Centered(image.Width * scale, image.Height * scale, w, h);
                }

            case WallpaperPosition.Center:
            case WallpaperPosition.Tile:
                return Centered(image.Width, image.Height, w, h);
            case WallpaperPosition.Span:
                {
                    var (vw, vh) = ((double)virtualScreen.Width, (double)virtualScreen.Height);
                    var scale = Math.Max(vw / image.Width, vh / image.Height);
                    var span = Centered(image.Width * scale, image.Height * scale, vw, vh);
                    span.Offset(virtualScreen.Left - monitor.Left, virtualScreen.Top - monitor.Top);
                    return span;
                }

            default:
                {
                    var scale = Math.Max(w / image.Width, h / image.Height);
                    return Centered(image.Width * scale, image.Height * scale, w, h);
                }
        }
    }

    private static Rect Centered(double width, double height, double areaWidth, double areaHeight) =>
        new((areaWidth - width) / 2, (areaHeight - height) / 2, width, height);

    private static void Draw(DrawingContext context, BitmapSource image, WallpaperPosition position, User32.Rect monitor, User32.Rect virtualScreen, int factor)
    {
        var size = new Size(image.PixelWidth, image.PixelHeight);
        if (position == WallpaperPosition.Tile)
        {
            var brush = new ImageBrush(image)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, size.Width / factor, size.Height / factor),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
            };
            context.DrawRectangle(brush, null, new Rect(0, 0, monitor.Width / (double)factor, monitor.Height / (double)factor));
            return;
        }

        var place = Place(position, size, monitor, virtualScreen);
        context.DrawImage(image, new Rect(place.X / factor, place.Y / factor, place.Width / factor, place.Height / factor));
    }

    /// <summary>O identificador do monitor no IDesktopWallpaper, pelo retângulo dele.</summary>
    private static string? FindMonitor(IDesktopWallpaper wallpaper, User32.Rect monitor)
    {
        var count = wallpaper.GetMonitorDevicePathCount();
        for (uint index = 0; index < count; index++)
        {
            var id = wallpaper.GetMonitorDevicePathAt(index);
            var rect = wallpaper.GetMonitorRECT(id);
            if (rect.Left == monitor.Left && rect.Top == monitor.Top && rect.Right == monitor.Right && rect.Bottom == monitor.Bottom)
            {
                return id;
            }
        }

        return null;
    }

    private static BitmapImage Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>COLORREF é 0x00BBGGRR.</summary>
    private static Color FromColorRef(uint value) =>
        Color.FromRgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
}
