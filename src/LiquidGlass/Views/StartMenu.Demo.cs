#if DEBUG
using System.Windows.Interop;
using LiquidGlass.Interop;

namespace LiquidGlass.Views;

/// <summary>Demonstrações (só no Debug) que App.StartDemos usa para testar o menu sem teclado.</summary>
public partial class StartMenu
{
    /// <summary>Digita na pesquisa como o teclado faria (a pesquisa roda depois da pausa normal).</summary>
    internal void DemoSearch(string text)
    {
        SearchBox.Text = text;
        SearchBox.CaretIndex = text.Length;
    }

    private System.Windows.Controls.ContextMenu? _demoPowerMenu;

    /// <summary>Abre (ou fecha) o menu do botão de energia, só para a foto: nenhum item é escolhido.</summary>
    internal void DemoPowerMenu(bool open)
    {
        if (open)
        {
            _demoPowerMenu = OpenPowerMenu(atMouse: false);
            return;
        }

        if (_demoPowerMenu is { } menu)
        {
            var items = menu.Items.OfType<System.Windows.Controls.MenuItem>()
                .Select(item => ((System.Windows.Controls.TextBlock)((System.Windows.Controls.StackPanel)item.Header).Children[1]).Text);
            Graphics.DemoCapture.Log($"start energia: itens=[{string.Join(", ", items)}] usuário=\"{UserName.Text}\"");
            menu.IsOpen = false;
            _demoPowerMenu = null;
        }
    }

    /// <summary>Fotografa o menu (e o de energia, se aberto) e anota em demo.log o que a foto não mostra.</summary>
    internal void SaveDemo(string name)
    {
        Graphics.DemoCapture.Save(name, Glass, _demoPowerMenu);
        var handle = new WindowInteropHelper(this).Handle;
        var results = string.Join(" | ", _results.Select(entry => $"{entry.Group}: {entry.Name} ({entry.Subtitle})"));
        Graphics.DemoCapture.Log(
            $"start {name}: aberto={IsMenuOpen} foco={User32.GetForegroundWindow() == handle} teclado={SearchBox.IsKeyboardFocused} " +
            $"fixados={_pins.Count} com ícone={_pins.Count(pin => pin.Icon is not null)} recentes={_recent.Count} apps={_apps.Count} " +
            $"pesquisa=\"{SearchBox.Text}\" resultados=[{results}]");
    }
}
#endif
