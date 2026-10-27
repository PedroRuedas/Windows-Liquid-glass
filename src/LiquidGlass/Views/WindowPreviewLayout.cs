using System.Windows;
using LiquidGlass.Models;

namespace LiquidGlass.Views;

/// <summary>
/// Tamanho dos cartões do painel de miniaturas. Com muitas janelas, os cartões quebram em linhas
/// (colunas, com a dock nas laterais) e encolhem até todos caberem na tela.
/// </summary>
internal static class WindowPreviewLayout
{
    /// <summary>Altura das miniaturas com a dock embaixo; nas laterais, a largura é fixa.</summary>
    public const double BottomThumbnailHeight = 112;
    public const double SideThumbnailWidth = 200;
    public const double MinThumbnailWidth = 96;
    public const double MaxThumbnailWidth = 208;
    public const double MinThumbnailHeight = 64;
    public const double MaxThumbnailHeight = 140;

    /// <summary>Menor escala dos cartões: abaixo disso, a miniatura já não diz qual janela é.</summary>
    public const double MinScale = 0.5;

    /// <summary>
    /// Espaço de cada cartão além da miniatura: margem, borda e respiro (WindowPreview.xaml) e,
    /// na altura, a linha do título.
    /// </summary>
    public static readonly Size CardChrome = new(22, 48);

    private const int ScaleSteps = 10;

    /// <summary>Miniatura de uma janela de <paramref name="source"/> pixels, com a proporção dela.</summary>
    public static Size ThumbnailSize(Size source, DockEdge edge, double scale)
    {
        var aspect = source.Width > 0 && source.Height > 0 ? source.Width / source.Height : 16.0 / 10;
        return edge == DockEdge.Bottom
            ? new Size(Math.Clamp(BottomThumbnailHeight * aspect, MinThumbnailWidth, MaxThumbnailWidth) * scale, BottomThumbnailHeight * scale)
            : new Size(SideThumbnailWidth * scale, Math.Clamp(SideThumbnailWidth / aspect, MinThumbnailHeight, MaxThumbnailHeight) * scale);
    }

    /// <summary>
    /// A maior escala (de 1 até <see cref="MinScale"/>) em que os cartões de <paramref name="sources"/>
    /// cabem em <paramref name="available"/>, e quantos cabem nela. Só sobra janela de fora com
    /// tantas abertas que nem na menor escala cabem todas.
    /// </summary>
    public static (double Scale, int Count) Fit(IReadOnlyList<Size> sources, DockEdge edge, Size available)
    {
        var scale = 1.0;
        var count = 0;
        for (var step = 0; step <= ScaleSteps; step++)
        {
            scale = 1 - ((1 - MinScale) * step / ScaleSteps);
            count = CountFitting(sources, edge, available, scale);
            if (count == sources.Count)
            {
                break;
            }
        }

        return (scale, count);
    }

    /// <summary>Quantos cartões cabem, preenchendo linhas ao longo da dock e empilhando-as para longe dela.</summary>
    private static int CountFitting(IReadOnlyList<Size> sources, DockEdge edge, Size available, double scale)
    {
        var (availableAlong, availableAcross) = edge == DockEdge.Bottom ? (available.Width, available.Height) : (available.Height, available.Width);
        double lineAlong = 0, lineAcross = 0, usedAcross = 0;
        for (var index = 0; index < sources.Count; index++)
        {
            var thumbnail = ThumbnailSize(sources[index], edge, scale);
            var card = new Size(thumbnail.Width + CardChrome.Width, thumbnail.Height + CardChrome.Height);
            var (along, across) = edge == DockEdge.Bottom ? (card.Width, card.Height) : (card.Height, card.Width);
            if (lineAlong > 0 && lineAlong + along > availableAlong)
            {
                usedAcross += lineAcross;
                (lineAlong, lineAcross) = (0, 0);
            }

            if (along > availableAlong || usedAcross + Math.Max(lineAcross, across) > availableAcross)
            {
                return index;
            }

            lineAlong += along;
            lineAcross = Math.Max(lineAcross, across);
        }

        return sources.Count;
    }
}
