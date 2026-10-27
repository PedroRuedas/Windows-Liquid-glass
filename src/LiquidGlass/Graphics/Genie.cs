using System.Windows;
using LiquidGlass.Models;

namespace LiquidGlass.Graphics;

/// <summary>
/// Efeito "lâmpada mágica" ao minimizar: a janela é fatiada em faixas ao longo do eixo da dock.
/// Primeiro as laterais se curvam em funil na direção do ícone (o lado perto da dock estreita mais);
/// depois as faixas escorrem pelo funil até caberem no ícone. Cada faixa vira uma miniatura do
/// DWM com o trecho correspondente da janela, então o conteúdo continua nítido e vivo.
/// </summary>
/// <param name="window">Janela, em pixels físicos da tela.</param>
/// <param name="icon">Ícone na dock, em pixels físicos da tela.</param>
/// <param name="source">Tamanho da janela de origem da miniatura.</param>
internal sealed class Genie(Rect window, Rect icon, Size source, DockEdge edge)
{
    /// <summary>Uma fatia a cada ~6 px: com menos, as bordas do funil ficam em degraus.</summary>
    public int StripCount { get; } = Math.Clamp((int)((edge == DockEdge.Bottom ? window.Height : window.Width) / 6), 30, 160);

    /// <summary>Destino e trecho da janela de cada fatia em <paramref name="t"/> (0 = janela, 1 = dentro do ícone).</summary>
    public IEnumerable<(Rect Destination, Int32Rect Source)> Layout(double t)
    {
        // Eixo "a" aponta da janela para a dock; "c" é o eixo transversal.
        var (windowFar, windowNear, windowStart, windowEnd) = Axes(window);
        var (iconFar, iconNear, iconStart, iconEnd) = Axes(icon);

        var bend = SmoothStep(Math.Clamp(t / 0.4, 0, 1));
        var near = Lerp(windowNear, iconNear, EaseInOut(Math.Clamp(t / 0.55, 0, 1)));
        var far = Lerp(windowFar, iconFar, EaseInOut(Math.Clamp((t - 0.15) / 0.85, 0, 1)));
        var funnelLength = Math.Max(iconFar - windowFar, 1);

        for (var index = 0; index < StripCount; index++)
        {
            var from = (double)index / StripCount;
            var to = (double)(index + 1) / StripCount;
            var a0 = Lerp(far, near, from);
            var a1 = Lerp(far, near, to);

            // Quanto mais perto do ícone, mais a fatia se estreita até a largura dele.
            var funnel = SmoothStep(Math.Clamp((((a0 + a1) / 2) - windowFar) / funnelLength, 0, 1)) * bend;
            var c0 = Lerp(windowStart, iconStart, funnel);
            var c1 = Lerp(windowEnd, iconEnd, funnel);
            yield return (ToScreen(a0, a1, c0, c1), SourceSlice(from, to));
        }
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);

    private static double SmoothStep(double t) => t * t * (3 - (2 * t));

    private static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);

    private (double Far, double Near, double Start, double End) Axes(Rect rect) => edge switch
    {
        DockEdge.Right => (rect.Left, rect.Right, rect.Top, rect.Bottom),
        DockEdge.Left => (-rect.Right, -rect.Left, rect.Top, rect.Bottom),
        _ => (rect.Top, rect.Bottom, rect.Left, rect.Right),
    };

    private Rect ToScreen(double a0, double a1, double c0, double c1)
    {
        var length = Math.Max(a1 - a0, 0);
        var span = Math.Max(c1 - c0, 0);
        return edge switch
        {
            DockEdge.Right => new Rect(a0, c0, length, span),
            DockEdge.Left => new Rect(-a1, c0, length, span),
            _ => new Rect(c0, a0, span, length),
        };
    }

    private Int32Rect SourceSlice(double from, double to)
    {
        var width = (int)source.Width;
        var height = (int)source.Height;
        return edge switch
        {
            DockEdge.Right => Slice(from, to, width, (start, size) => new Int32Rect(start, 0, size, height)),
            DockEdge.Left => Slice(1 - to, 1 - from, width, (start, size) => new Int32Rect(start, 0, size, height)),
            _ => Slice(from, to, height, (start, size) => new Int32Rect(0, start, width, size)),
        };
    }

    private static Int32Rect Slice(double from, double to, int length, Func<int, int, Int32Rect> create)
    {
        var start = (int)Math.Round(from * length);
        var end = (int)Math.Round(to * length);
        return create(start, Math.Max(end - start, 1));
    }
}
