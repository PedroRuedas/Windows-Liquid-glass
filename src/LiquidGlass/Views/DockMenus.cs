using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LiquidGlass.Models;
using LiquidGlass.Services;
using LiquidGlass.ViewModels;

namespace LiquidGlass.Views;

/// <summary>Monta os menus de contexto da dock no momento em que são abertos.</summary>
internal static class DockMenus
{
    private const int MaxTitleLength = 48;

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
                menu.Items.Add(Command(item.IsRunning ? "Nova janela" : "Abrir", item.OpenNewCommand));
            }

            menu.Items.Add(Command(item.IsPinned ? "Remover da Dock" : "Manter na Dock", item.TogglePinCommand));

            if (item.IsRunning)
            {
                menu.Items.Add(Command(item.Windows.Count > 1 ? "Fechar todas as janelas" : "Fechar", item.CloseAllCommand));
            }

            menu.Items.Add(new Separator());
        }

        if (dock.Updates is { } updates)
        {
            menu.Items.Add(UpdateItem(updates, dock));
        }

        menu.Items.Add(Command("Personalizar a Dock…", dock.OpenSettingsCommand));
        menu.Items.Add(Command("Sair do Liquid Glass", dock.QuitCommand));
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
            var title = category.IsTasks ? (hasGroups ? "Tarefas" : null) : category.Name;
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
                menuItem.Click += (_, _) => JumpListService.Launch(entry);
                menu.Items.Add(menuItem);
            }

            menu.Items.Add(new Separator());
        }
    }

    /// <summary>"Nova janela", "Abrir nova janela"; não "Nova janela anônima", que é outra coisa.</summary>
    private static bool IsNewWindowTask(JumpListEntry entry)
    {
        return entry.Title.EndsWith("nova janela", StringComparison.CurrentCultureIgnoreCase)
            || entry.Title.EndsWith("new window", StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Instala a versão nova já encontrada ou procura uma; durante o trabalho, mostra o andamento.</summary>
    private static MenuItem UpdateItem(UpdateService updates, DockViewModel dock)
    {
        return updates.State switch
        {
            UpdateState.Checking => new MenuItem { Header = "Procurando atualizações…", IsEnabled = false },
            UpdateState.Downloading => new MenuItem
            {
                Header = string.Create(CultureInfo.CurrentCulture, $"Baixando atualização… {updates.Progress:0%}"),
                IsEnabled = false,
            },
            UpdateState.Installing => new MenuItem { Header = "Instalando atualização…", IsEnabled = false },
            _ when updates.Available is { } release => new MenuItem
            {
                Header = string.Create(CultureInfo.CurrentCulture, $"Atualizar para a versão {release.Version}"),
                Command = dock.InstallUpdateCommand,
                FontWeight = FontWeights.SemiBold,
            },
            _ => Command("Procurar atualizações…", dock.CheckForUpdatesCommand),
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
