using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiquidGlass.Graphics;
using LiquidGlass.Interop;
using LiquidGlass.Models;

namespace LiquidGlass.Services;

/// <summary>
/// Retrato desfocado do que está na tela, tirado logo antes de abrir um menu ou painel. É o fundo
/// que o vidro desses popups refrata. Como eles abrem sobre janelas (e não só sobre o papel de
/// parede, como a dock), precisam do conteúdo real atrás deles.
/// </summary>
public sealed partial class ScreenBackdrop : ObservableObject
{
    /// <summary>Até onde um menu ou painel pode se estender a partir da dock, em pixels físicos.</summary>
    private const double PopupReach = 600;

    /// <summary>Metade da largura máxima de um popup ao longo da dock (ele abre centralizado no ícone).</summary>
    private const double PopupHalfSpan = 280;

    /// <summary>Menus abertos no cursor podem ir para qualquer lado; a margem cobre todos.</summary>
    private static readonly Size CursorReach = new(320, 560);

    /// <summary>Desfoque equivalente a ~14 px na tela, aplicado na imagem reduzida.</summary>
    private const int BlurRadius = 4;

    /// <summary>
    /// A captura é reduzida 4× antes do desfoque: abrir um menu custava ~55 ms de trabalho na
    /// thread da interface (a dock inteira travava), e o fundo desfocado fica visualmente igual.
    /// </summary>
    private const int Downscale = 4;

    [ObservableProperty]
    private ImageSource? _image;

    /// <summary>Canto superior esquerdo de <see cref="Image"/> na tela, em pixels físicos.</summary>
    [ObservableProperty]
    private Point _origin;

    public static ScreenBackdrop Shared { get; } = new();

    /// <summary>
    /// Captura só o lado da âncora para onde os popups da dock abrem. Ler a tela é a parte cara
    /// (cresce com a área), então a região cobre o maior popup e nada além.
    /// </summary>
    public void CaptureBeside(FrameworkElement anchor, DockEdge edge)
    {
        var bounds = ScreenBounds(anchor);
        var center = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        var region = edge switch
        {
            DockEdge.Left => new Rect(bounds.Left, center.Y - PopupHalfSpan, bounds.Width + PopupReach, PopupHalfSpan * 2),
            DockEdge.Right => new Rect(bounds.Left - PopupReach, center.Y - PopupHalfSpan, bounds.Width + PopupReach, PopupHalfSpan * 2),
            _ => new Rect(center.X - PopupHalfSpan, bounds.Top - PopupReach, PopupHalfSpan * 2, bounds.Height + PopupReach),
        };

        Capture(region);
    }

    /// <summary>Captura em volta do cursor (menus abertos no ponto do clique).</summary>
    public void CaptureAroundCursor()
    {
        User32.GetCursorPos(out var cursor);
        Capture(Rect.Inflate(new Rect(cursor.X, cursor.Y, 1, 1), CursorReach.Width, CursorReach.Height));
    }

    private static Rect ScreenBounds(FrameworkElement element)
    {
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }

    /// <summary>Captura e desfoca <paramref name="region"/> (em pixels físicos), limitada ao monitor.</summary>
    private void Capture(Rect region)
    {
        var monitor = User32.GetPrimaryMonitorInfo().Monitor;
        region.Intersect(new Rect(monitor.Left, monitor.Top, monitor.Width, monitor.Height));
        if (region.IsEmpty || region.Width < 1 || region.Height < 1)
        {
            return;
        }

        var bounds = new Int32Rect((int)region.X, (int)region.Y, (int)region.Width, (int)region.Height);
        var scaled = ScreenCapture.Screen(bounds, Downscale, out var width, out var height);
        BoxBlur.Apply(scaled, width, height, BlurRadius);

        // DPI reduzido na mesma proporção: em DIPs a imagem continua do tamanho da região
        // capturada, então o vidro mapeia o recorte em pixels de tela como antes.
        const double Dpi = 96.0 / Downscale;
        var bitmap = BitmapSource.Create(width, height, Dpi, Dpi, PixelFormats.Bgra32, null, scaled, width * 4);
        bitmap.Freeze();
        Origin = new Point(bounds.X, bounds.Y);
        Image = bitmap;
    }
}
