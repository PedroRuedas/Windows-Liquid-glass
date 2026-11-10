using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LiquidGlass.Models;
using LiquidGlass.Resources;
using LiquidGlass.Services;
using LiquidGlass.ViewModels;

namespace LiquidGlass.Views;

/// <summary>Monta os menus de contexto da dock no momento em que são abertos.</summary>
internal static class DockMenus
{
    private const int MaxTitleLength = 48;

    /// <summary>"Nova janela" em português, inglês, espanhol, francês, alemão e italiano.</summary>
    private static readonly string[] NewWindowTitles = ["nova janela", "new window", "nueva ventana", "nouvelle fenêtre", "neues fenster", "nuova finestra"];

    /// <summary>
    /// Preenche o menu. Com <paramref name="anchor"/>, o menu abre ao lado do ícone, voltado para
    /// dentro da tela; sem ele, abre no cursor.
    /// </summary>
    public static void Populate(ContextMenu menu, DockItemViewModel? item, DockViewModel dock, UIElement? anchor = null)
    {
        menu.Items.Clear();
        menu.CustomPopupPlacementCallback = (popup, target, _) => DockPopupPlacement.Beside(dock.Settings.Edge, popup, target);
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
            var jumpList = JumpListService.Load(item.IsRunning ? item.Windows[0] : null, item.Pin);
            AddJumpList(menu, jumpList);

            // A maioria dos apps com tarefas já oferece "Nova janela" do jeito deles (com perfil, por exemplo).
            if (!item.IsRunning || !jumpList.Any(category => category.IsTasks && category.Entries.Any(IsNewWindowTask)))
            {
                menu.Items.Add(Command(item.IsRunning ? Strings.NewWindow : Strings.Open, item.OpenNewCommand));
            }

            menu.Items.Add(Command(item.IsPinned ? Strings.RemoveFromDock : Strings.KeepInDock, item.TogglePinCommand));

            if (item.IsRunning)
            {
                menu.Items.Add(Command(item.Windows.Count > 1 ? Strings.CloseAllWindows : Strings.Close, item.CloseAllCommand));
            }

            menu.Items.Add(new Separator());
        }

        if (dock.Updates is { } updates)
        {
            menu.Items.Add(UpdateItem(updates, dock));
        }

        menu.Items.Add(Command(Strings.CustomizeDockMenu, dock.OpenSettingsCommand));
        menu.Items.Add(Command(Strings.QuitDock, dock.QuitCommand));
    }

    /// <summary>
    /// Grupos ("Pastas recentes") e tarefas ("Nova guia", "Nova janela InPrivate") que o app
    /// registrou para a barra de tarefas do Windows, na mesma ordem que ela mostra.
    /// </summary>
    private static void AddJumpList(ContextMenu menu, IReadOnlyList<JumpListCategory> jumpList)
    {
        var hasGroups = jumpList.Any(category => !category.IsTasks);
        foreach (var category in jumpList.OrderBy(category => category.IsTasks))
        {
            var title = category.IsTasks ? (hasGroups ? Strings.JumpListTasks : null) : category.Name;
            if (title is { Length: > 0 })
            {
                menu.Items.Add(new MenuItem { Header = title, IsEnabled = false });
            }

            foreach (var entry in category.Entries)
            {
                if (entry.IsSeparator)
                {
                    menu.Items.Add(new Separator());
                    continue;
                }

                var menuItem = new MenuItem { Header = Shorten(entry.Title) };
                menuItem.Click += (_, _) => Launch(entry);
                menu.Items.Add(menuItem);
            }

            menu.Items.Add(new Separator());
        }
    }

    /// <summary>A barra de tarefas avisa quando o item não abre; sem o aviso, o clique pareceria ignorado.</summary>
    private static void Launch(JumpListEntry entry)
    {
        if (!JumpListService.Launch(entry))
        {
            MessageBox.Show(
                Strings.JumpListOpenFailed(entry.Title),
                "Liquid Glass Dock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// "Nova janela", "Abrir nova janela", "Nueva ventana"; não "Nova janela anônima", que é outra
    /// coisa. O título vem no idioma do app, que pode não ser o do Windows: daí os idiomas mais comuns.
    /// </summary>
    internal static bool IsNewWindowTask(JumpListEntry entry)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        var title = entry.Title.Trim();
        return NewWindowTitles.Any(suffix => compare.IsSuffix(title, suffix, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace));
    }

    /// <summary>Instala a versão nova já encontrada ou procura uma; durante o trabalho, mostra o andamento.</summary>
    private static MenuItem UpdateItem(UpdateService updates, DockViewModel dock)
    {
        return updates.State switch
        {
            UpdateState.Checking => new MenuItem { Header = Strings.MenuCheckingForUpdates, IsEnabled = false },
            UpdateState.Downloading => new MenuItem
            {
                Header = Strings.MenuDownloadingUpdate(updates.Progress),
                IsEnabled = false,
            },
            UpdateState.Installing => new MenuItem { Header = Strings.MenuInstallingUpdate, IsEnabled = false },
            _ when updates.Available is { } release => new MenuItem
            {
                Header = Strings.MenuUpdateTo(release.Version),
                Command = dock.InstallUpdateCommand,
                FontWeight = FontWeights.SemiBold,
            },
            _ => Command(Strings.MenuCheckForUpdates, dock.CheckForUpdatesCommand),
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
