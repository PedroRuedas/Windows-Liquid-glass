using System.Windows;
using System.Windows.Controls.Primitives;
using LiquidGlass.Models;

namespace LiquidGlass.Views;

/// <summary>Posiciona menus e painéis da dock centralizados no ícone, do lado voltado para o meio da tela.</summary>
internal static class DockPopupPlacement
{
    /// <summary>Os templates têm 14 DIPs de margem para a sombra; isto deixa ~6 visíveis entre o ícone e o painel.</summary>
    private const double AnchorGap = -8;

    public static CustomPopupPlacement[] Beside(DockEdge edge, Size popup, Size target)
    {
        var placement = edge switch
        {
            DockEdge.Left => new CustomPopupPlacement(
                new Point(target.Width + AnchorGap, (target.Height - popup.Height) / 2),
                PopupPrimaryAxis.Vertical),
            DockEdge.Right => new CustomPopupPlacement(
                new Point(-popup.Width - AnchorGap, (target.Height - popup.Height) / 2),
                PopupPrimaryAxis.Vertical),
            _ => new CustomPopupPlacement(
                new Point((target.Width - popup.Width) / 2, -popup.Height - AnchorGap),
                PopupPrimaryAxis.Horizontal),
        };

        return [placement];
    }
}
