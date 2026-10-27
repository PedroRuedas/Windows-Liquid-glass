using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LiquidGlass.Interop;

namespace LiquidGlass.Views;

/// <summary>
/// Retrato de uma janela que acabou de fechar, tocando a animação de fechar do GNOME: encolhe para
/// 80% enquanto some. A janela de verdade já não existe; esta só vive durante a animação.
/// </summary>
internal sealed class CloseGhost : Window
{
    private const double EndScale = 0.8;

    private readonly Rect _bounds;

    private CloseGhost(ImageSource snapshot, Rect bounds)
    {
        _bounds = bounds;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Content = new Image
        {
            Source = snapshot,
            Stretch = Stretch.Fill,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(),
        };

        SourceInitialized += OnSourceInitialized;
    }

    /// <param name="bounds">Área da janela na tela, em pixels físicos.</param>
    public static void Play(ImageSource snapshot, Rect bounds, TimeSpan duration)
    {
        var ghost = new CloseGhost(snapshot, bounds);
        ghost.Show();
        ghost.Animate(duration);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        User32.AddExtendedStyle(source.Handle, User32.WsExToolWindow | User32.WsExNoActivate | User32.WsExTransparent);
        User32.SetWindowPos(
            source.Handle, User32.HwndTopMost,
            (int)_bounds.X, (int)_bounds.Y, (int)_bounds.Width, (int)_bounds.Height,
            User32.SwpNoActivate);
    }

    private void Animate(TimeSpan duration)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var image = (Image)Content;
        var scale = (ScaleTransform)image.RenderTransform;
        var fade = new DoubleAnimation(0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) => Close();

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(EndScale, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(EndScale, duration) { EasingFunction = ease });
        image.BeginAnimation(OpacityProperty, fade);
    }
}
