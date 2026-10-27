#if DEBUG
using System.Windows;
using LiquidGlass.Controls;
using LiquidGlass.ViewModels;

namespace LiquidGlass.Views;

/// <summary>Demonstrações (só no Debug) que App.StartDemos usa para testar e fotografar sem mouse.</summary>
public partial class DockWindow
{
    /// <summary>
    /// Demonstração (Debug): abre as miniaturas do app aberto com esse nome, como o mouse parado
    /// faria, e as mantém abertas sem o mouse; sem nome, fecha.
    /// </summary>
    internal void DemoPreview(string? name)
    {
        _preview.IsDemo = name is not null;
        if (name is null)
        {
            _preview.CloseAnimated();
            return;
        }

        if (_source is not null
            && _viewModel.Items.FirstOrDefault(item => item.IsRunning && item.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) is { } item
            && DockItems.ItemContainerGenerator.ContainerFromItem(item) is DockItemView view)
        {
            _preview.Show(view, item, Settings.Edge, _source.Handle);
        }
    }

    internal void SaveDemoPreview(string name) => _preview.SaveDemoFrame(name);

    /// <summary>
    /// Demonstração (Debug): abre o menu do botão direito do app com esse nome, como o clique faria,
    /// e o fotografa junto com o ícone; sem nome, fecha.
    /// </summary>
    internal void DemoMenu(string? name)
    {
        if (name is null)
        {
            if (_openMenu is { } open)
            {
                Graphics.DemoCapture.Save("menu", open, open.PlacementTarget as FrameworkElement);
                open.IsOpen = false;
            }

            return;
        }

        if (_viewModel.Items.FirstOrDefault(item => item.IsApplication && item.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) is { } item
            && DockItems.ItemContainerGenerator.ContainerFromItem(item) is DockItemView { ContextMenu: { } menu } view)
        {
            PrepareItemMenu(view, item, menu);
            menu.PlacementTarget = view;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
            menu.StaysOpen = true;
            menu.IsOpen = true;
        }
    }

    /// <summary>Demonstração (Debug): devolve o painel ao mouse e fotografa o recolhimento, se acontecer.</summary>
    internal void DemoReleasePreview()
    {
        _preview.IsDemo = false;
        _preview.CaptureNextCollapse = true;
    }
}
#endif
