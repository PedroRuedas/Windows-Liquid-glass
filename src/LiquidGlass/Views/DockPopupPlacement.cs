using System.Windows;
using System.Windows.Controls.Primitives;
using LiquidGlass.Models;

namespace LiquidGlass.Views;

/// <summary>Posiciona menus e painéis da dock centralizados no ícone, do lado voltado para o meio da tela.</summary>
internal static class DockPopupPlacement
{
    /// <summary>Os templates têm 14 DIPs de margem para a sombra; isto deixa ~6 visíveis entre o ícone e o menu.</summary>
    public const double MenuGap = -8;

    /// <summary>
    /// Painéis que ficam abertos mantêm toda a sombra fora do ícone: assim ela nunca cobre o botão
    /// que os abriu, e o segundo clique nele chega à dock.
    /// </summary>
    public const double PanelGap = 6;

    public static CustomPopupPlacement[] Beside(DockEdge edge, Size popup, Size target, double gap = MenuGap)
    {
        var placement = edge switch
        {
            DockEdge.Left => new CustomPopupPlacement(
                new Point(target.Width + gap, (target.Height - popup.Height) / 2),
                PopupPrimaryAxis.Vertical),
            DockEdge.Right => new CustomPopupPlacement(
                new Point(-popup.Width - gap, (target.Height - popup.Height) / 2),
                PopupPrimaryAxis.Vertical),
            _ => new CustomPopupPlacement(
                new Point((target.Width - popup.Width) / 2, -popup.Height - gap),
                PopupPrimaryAxis.Horizontal),
        };

        return [placement];
    }
}
