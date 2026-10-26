using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiquidGlass.ViewModels;

namespace LiquidGlass.Views;

/// <summary>Monta os menus de contexto da dock no momento em que são abertos.</summary>
internal static class DockMenus
{
    private const int MaxTitleLength = 48;

    public static void Populate(ContextMenu menu, DockItemViewModel? item, DockViewModel dock)
    {
        menu.Items.Clear();

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

    private static MenuItem Command(string header, ICommand command, object? parameter = null)
    {
        return new MenuItem { Header = header, Command = command, CommandParameter = parameter };
    }

    private static string Shorten(string title)
    {
        return title.Length <= MaxTitleLength ? title : string.Concat(title.AsSpan(0, MaxTitleLength - 1), "…");
    }
}
