using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiquidGlass.Graphics;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>
/// Retrato desfocado do que está na tela, tirado logo antes de abrir um menu ou painel. É o fundo
/// que o vidro desses popups refrata. Como eles abrem sobre janelas (e não só sobre o papel de
/// parede, como a dock), precisam do conteúdo real atrás deles.
/// </summary>
public sealed partial class ScreenBackdrop : ObservableObject
{
    /// <summary>Margem capturada em volta da âncora: cobre o maior menu ou painel que pode abrir ali.</summary>
    private static readonly Size Reach = new(480, 680);

    private const int BlurRadius = 14;

    [ObservableProperty]
    private ImageSource? _image;

    /// <summary>Canto superior esquerdo de <see cref="Image"/> na tela, em pixels físicos.</summary>
    [ObservableProperty]
    private Point _origin;

    public static ScreenBackdrop Shared { get; } = new();

    /// <summary>Captura a região em volta de <paramref name="anchor"/> (retângulo em pixels físicos).</summary>
    public void CaptureAround(Rect anchor)
    {
        var monitor = User32.GetPrimaryMonitorInfo().Monitor;
        var area = new Rect(monitor.Left, monitor.Top, monitor.Width, monitor.Height);
        var region = Rect.Inflate(anchor, Reach.Width, Reach.Height);
        region.Intersect(area);
        if (region.IsEmpty || region.Width < 1 || region.Height < 1)
        {
            return;
        }

        var bounds = new Int32Rect((int)region.X, (int)region.Y, (int)region.Width, (int)region.Height);
        var pixels = ScreenCapture.Screen(bounds);
        BoxBlur.Apply(pixels, bounds.Width, bounds.Height, BlurRadius);

        var bitmap = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgra32, null, pixels, bounds.Width * 4);
        bitmap.Freeze();
        Origin = new Point(bounds.X, bounds.Y);
        Image = bitmap;
    }

    /// <summary>Captura em volta de um elemento já visível na tela.</summary>
    public void CaptureAround(FrameworkElement element)
    {
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        CaptureAround(new Rect(topLeft, bottomRight));
    }

    /// <summary>Captura em volta do cursor (menus abertos no ponto do clique).</summary>
    public void CaptureAroundCursor()
    {
        User32.GetCursorPos(out var cursor);
        CaptureAround(new Rect(cursor.X, cursor.Y, 1, 1));
    }
}
