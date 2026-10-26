using System.Windows;
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
    /// <summary>
    /// O fundo da dock é sempre desfocado, então meia resolução fica visualmente igual e ocupa ¼
    /// da memória (na RAM e na placa de vídeo).
    /// </summary>
    private const int Downscale = 2;

    private byte[]? _pixels;
    private int _width;
    private int _height;
    private int _blurRadius = -1;
    private int _version;

    /// <summary>Disparado sempre que <see cref="Backdrop"/> muda.</summary>
    public event EventHandler? Changed;

    /// <summary>Papel de parede desfocado; em DIPs, 1 unidade = 1 pixel físico do monitor.</summary>
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
        // O raio vem em pixels de tela; a imagem guardada é menor.
        var radius = (int)Math.Round(Math.Max(blurRadius, 0) / Downscale);
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

            // DPI reduzido na mesma proporção mantém o tamanho em DIPs igual ao do monitor.
            const double Dpi = 96.0 / Downscale;
            var bitmap = BitmapSource.Create(width, height, Dpi, Dpi, PixelFormats.Bgra32, null, copy, width * 4);
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
        _pixels = ScreenCapture.Wallpaper(
            Math.Max(monitor.Width, 1),
            Math.Max(monitor.Height, 1),
            Downscale,
            out _width,
            out _height);
    }
}
