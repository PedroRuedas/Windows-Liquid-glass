using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LiquidGlass.Models;

namespace LiquidGlass.Views;

/// <summary>
/// Entrada e saída dos painéis da dock que fecham sozinhos (segundo plano e miniaturas das
/// janelas), para os dois se comportarem igual. Anima só o "PopupContent" de cada um.
/// </summary>
internal static class DockPopupAnimation
{
    /// <summary>Tempo para levar o mouse do ícone até o painel (ou voltar) sem que ele feche.</summary>
    public static readonly TimeSpan HoverGrace = TimeSpan.FromMilliseconds(400);

    /// <summary>Duração da entrada (PopupOpenStoryboard), com folga.</summary>
    public static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(0.36);

    public static readonly TimeSpan CloseDuration = TimeSpan.FromSeconds(0.24);

    /// <summary>Escala final ao recolher: largura e altura para a dock embaixo (invertidas nas laterais).</summary>
    private const double CollapsedWidth = 0.78;
    private const double CollapsedHeight = 0.35;

    /// <summary>Fração da animação de recolher em que o painel começa a sumir.</summary>
    private const double FadeStart = 0.25;

    /// <summary>Cresce a partir da dock enquanto aparece.</summary>
    public static void Open(FrameworkElement owner, FrameworkElement content, DockEdge edge)
    {
        content.RenderTransformOrigin = edge switch
        {
            DockEdge.Left => new Point(0, 0.5),
            DockEdge.Right => new Point(1, 0.5),
            _ => new Point(0.5, 1),
        };

        // Remove o estado final de um fechamento animado anterior antes de animar a entrada.
        var scale = (ScaleTransform)content.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        content.BeginAnimation(UIElement.OpacityProperty, null);

        var storyboard = (Storyboard)owner.FindResource("PopupOpenStoryboard");
        storyboard.Begin(owner);
    }

    /// <summary>
    /// Recolhe para dentro do ícone: um leve respiro para fora responde na hora, e então o painel é
    /// puxado, achatando mais no eixo da dock. Só some depois que a contração já está visível.
    /// </summary>
    public static void Collapse(FrameworkElement content, DockEdge edge, Action completed)
    {
        var (toScaleX, toScaleY) = edge == DockEdge.Bottom ? (CollapsedWidth, CollapsedHeight) : (CollapsedHeight, CollapsedWidth);
        var pull = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.3 };
        var scaleX = new DoubleAnimation(toScaleX, CloseDuration) { EasingFunction = pull };
        var scaleY = new DoubleAnimation(toScaleY, CloseDuration) { EasingFunction = pull };

        var fade = new DoubleAnimationUsingKeyFrames { Duration = CloseDuration };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(FadeStart)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        fade.Completed += (_, _) => completed();

        var scale = (ScaleTransform)content.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        content.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
