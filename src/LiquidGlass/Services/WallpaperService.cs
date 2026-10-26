using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiquidGlass.Graphics;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>
/// Mantém uma cópia (já desfocada) do papel de parede do monitor principal, exatamente como o
/// Windows o desenha. É o "fundo" que o vidro líquido refrata.
/// </summary>
public sealed class WallpaperService
{
    private byte[]? _pixels;
    private int _width;
    private int _height;
    private int _blurRadius = -1;
    private int _version;

    /// <summary>Disparado sempre que <see cref="Backdrop"/> muda.</summary>
    public event EventHandler? Changed;

    /// <summary>Papel de parede desfocado, com 1 unidade = 1 pixel físico do monitor.</summary>
    public BitmapSource? Backdrop { get; private set; }

    /// <summary>Origem do monitor principal em pixels físicos.</summary>
    public Point MonitorOrigin { get; private set; }

    /// <summary>Captura o papel de parede novamente (troca de imagem, resolução ou tema).</summary>
    public Task RefreshAsync(double blurRadius)
    {
        Capture();
        _blurRadius = -1;
        return SetBlurAsync(blurRadius);
    }

    /// <summary>Aplica um novo raio de desfoque sobre a última captura.</summary>
    public async Task SetBlurAsync(double blurRadius)
    {
        var radius = (int)Math.Round(Math.Max(blurRadius, 0));
        if (_pixels is null || radius == _blurRadius)
        {
            return;
        }

        _blurRadius = radius;
        var version = ++_version;
        var source = _pixels;
        var (width, height) = (_width, _height);

        var blurred = await Task.Run(() =>
        {
            var copy = (byte[])source.Clone();
            BoxBlur.Apply(copy, width, height, radius);
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, copy, width * 4);
            bitmap.Freeze();
            return bitmap;
        });

        // Ignora resultados atrasados quando o usuário arrasta o controle deslizante rapidamente.
        if (version != _version)
        {
            return;
        }

        Backdrop = blurred;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Capture()
    {
        var monitor = User32.GetPrimaryMonitorInfo().Monitor;
        MonitorOrigin = new Point(monitor.Left, monitor.Top);
        _width = Math.Max(monitor.Width, 1);
        _height = Math.Max(monitor.Height, 1);

        var screenDc = User32.GetDC(0);
        var memoryDc = Gdi32.CreateCompatibleDC(screenDc);
        var bitmap = Gdi32.CreateCompatibleBitmap(screenDc, _width, _height);
        var previous = Gdi32.SelectObject(memoryDc, bitmap);

        try
        {
            User32.PaintDesktop(memoryDc);
            Gdi32.SelectObject(memoryDc, previous);

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            _pixels = new byte[_width * _height * 4];
            converted.CopyPixels(_pixels, _width * 4, 0);

            // Bitmaps GDI não preenchem o canal alfa; o fundo é sempre opaco.
            for (var alpha = 3; alpha < _pixels.Length; alpha += 4)
            {
                _pixels[alpha] = 255;
            }
        }
        finally
        {
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(memoryDc);
            User32.ReleaseDC(0, screenDc);
        }
    }
}
