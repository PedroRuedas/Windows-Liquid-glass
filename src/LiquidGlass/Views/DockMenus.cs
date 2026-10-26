using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LiquidGlass.Models;
using LiquidGlass.ViewModels;

namespace LiquidGlass.Views;

/// <summary>Monta os menus de contexto da dock no momento em que são abertos.</summary>
internal static class DockMenus
{
    private const int MaxTitleLength = 48;

    /// <summary>O template do menu tem 14 DIPs de margem para a sombra; isto deixa ~6 visíveis.</summary>
    private const double AnchorGap = -8;

    /// <summary>
    /// Preenche o menu. Com <paramref name="anchor"/>, o menu abre ao lado do ícone, voltado para
    /// dentro da tela; sem ele, abre no cursor.
    /// </summary>
    public static void Populate(ContextMenu menu, DockItemViewModel? item, DockViewModel dock, UIElement? anchor = null)
    {
        menu.Items.Clear();
        menu.CustomPopupPlacementCallback = (popup, target, _) => [PlaceBeside(dock.Settings.Edge, popup, target)];
        if (anchor is not null)
        {
            // O ContextMenuService usa a posição definida no dono do menu, não no próprio menu.
            ContextMenuService.SetPlacement(anchor, PlacementMode.Custom);
        }

        if (item is { IsApplication: true })
        {
            menu.Items.Add(new MenuItem { Header = item.Name, IsEnabled = false, FontWeight = FontWeights.SemiBold });

            foreach (var window in item.Windows)
            {
                menu.Items.Add(Command(Shorten(window.Title), item.ActivateWindowCommand, window));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(Command(item.IsRunning ? "Nova janela" : "Abrir", item.OpenNewCommand));
            menu.Items.Add(Command(item.IsPinned ? "Remover da Dock" : "Manter na Dock", item.TogglePinCommand));

            if (item.IsRunning)
            {
                menu.Items.Add(Command(item.Windows.Count > 1 ? "Fechar todas as janelas" : "Fechar", item.CloseAllCommand));
            }

            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Command("Personalizar a Dock…", dock.OpenSettingsCommand));
        menu.Items.Add(Command("Sair do Liquid Glass", dock.QuitCommand));
    }

    /// <summary>Centraliza o menu no ícone, do lado voltado para o meio da tela.</summary>
    private static CustomPopupPlacement PlaceBeside(DockEdge edge, Size popup, Size target)
    {
        return edge switch
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
    }

    private static MenuItem Command(string header, ICommand command, object? parameter = null)
    {
        return new MenuItem { Header = header, Command = command, CommandParameter = parameter };
    }

    private static string Shorten(string title)
    {
        return title.Length <= MaxTitleLength ? title : string.Concat(title.AsSpan(0, MaxTitleLength - 1), "…");
    }
}
