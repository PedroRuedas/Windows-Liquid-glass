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

    /// <summary>Fotografa o menu e anota em demo.log o que a foto não mostra.</summary>
    internal void SaveDemo(string name)
    {
        Graphics.DemoCapture.Save(name, Glass);
        var handle = new WindowInteropHelper(this).Handle;
        var results = string.Join(" | ", _results.Select(entry => $"{entry.Group}: {entry.Name} ({entry.Subtitle})"));
        Graphics.DemoCapture.Log(
            $"start {name}: aberto={IsMenuOpen} foco={User32.GetForegroundWindow() == handle} teclado={SearchBox.IsKeyboardFocused} " +
            $"fixados={_pins.Count} com ícone={_pins.Count(pin => pin.Icon is not null)} recentes={_recent.Count} apps={_apps.Count} " +
            $"pesquisa=\"{SearchBox.Text}\" resultados=[{results}]");
    }
}
#endif
