#if DEBUG
using System.Windows;
using System.Windows.Input;
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

    private int _demoHandledMoves;

    private void DemoCountHandledMove(MouseEventArgs e)
    {
        if (e.Handled)
        {
            _demoHandledMoves++;
        }
    }

    /// <summary>
    /// Demonstração (Debug): faz com o app desse nome o que o mouse faria — aperta, arrasta
    /// <paramref name="slots"/> vagas (negativo para trás) — pelos mesmos métodos que os eventos do
    /// mouse chamam, sem capturar o mouse de verdade. Antes confere que um movimento que o botão
    /// já marcou como tratado ainda chega à dock. Devolve a posição original do app no grupo.
    /// </summary>
    internal int DemoReorder(string name, double slots)
    {
        if (_panel is null
            || _viewModel.Items.FirstOrDefault(item => item.IsApplication && item.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) is not { } item
            || DockItems.ItemContainerGenerator.ContainerFromItem(item) is not DockItemView view)
        {
            return -1;
        }

        _demoHandledMoves = 0;
        view.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseMoveEvent, Handled = true });
        Graphics.DemoCapture.Log($"reorder: movimento já tratado chegou à dock: {_demoHandledMoves == 1}");

        var original = _viewModel.ReorderGroup(item).IndexOf(item);
        var start = SlotCenter(view);
        _captureOnReorder = false;
        Press(view, _panel.TranslatePoint(ToPanelPoint(start), Root));

        // Em passos de 4 DIPs, como um arrasto de verdade: abaixo do limiar ainda é um clique.
        var step = Settings.IconSize + Settings.IconSpacing;
        for (var moved = 0.0; Math.Abs(moved) <= Math.Abs(slots * step); moved += Math.Sign(slots) * 4)
        {
            var point = ToPanelPoint(start + moved);
            OnPointerMoved(point, _panel.TranslatePoint(point, Root), leftPressed: true);
            _panel.UpdateLayout();
            if (Math.Abs(moved) == 4)
            {
                Graphics.DemoCapture.Log($"reorder: arrasto começou com 4 DIPs: {_reorderView is not null}");
            }
        }

        Graphics.DemoCapture.Log($"reorder: arrastando: {_reorderView is not null}; grupo: {string.Join(", ", _viewModel.ReorderGroup(item).Select(i => i.Name))}");
        return original;
    }

    /// <summary>Demonstração (Debug): solta o botão e, se pedido, devolve o app à posição original.</summary>
    internal void DemoDrop(string name, int restoreTo)
    {
        Release();
        _captureOnReorder = true;
        if (restoreTo >= 0 && _viewModel.Items.FirstOrDefault(item => item.IsApplication && item.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) is { } item)
        {
            _viewModel.MoveWithinGroup(item, restoreTo);
            Graphics.DemoCapture.Log($"reorder: restaurado; grupo: {string.Join(", ", _viewModel.ReorderGroup(item).Select(i => i.Name))}");
        }
        else
        {
            Graphics.DemoCapture.Log($"reorder: solto; ainda arrastando: {_reorderView is not null}");
        }
    }

    internal StartMenu DemoStartMenu => _startMenu;

    internal void SaveDemoStack(string name)
    {
        Graphics.DemoCapture.Save(name, _stackFlyout.Child as FrameworkElement);
        _stackFlyout.IsDemo = false;
    }

    internal void HoldDemoStack() => _stackFlyout.IsDemo = true;

    /// <summary>
    /// Demonstração (Debug): leva o "mouse" da ampliação a uma fração do comprimento dos ícones
    /// (0 = início, 1 = fim) pelo mesmo caminho do movimento real, sem mexer no mouse; null solta.
    /// </summary>
    internal void DemoHover(double? fraction)
    {
        if (_panel is null)
        {
            return;
        }

        if (fraction is not { } along)
        {
            OnRootMouseLeave(this, null!);
            return;
        }

        _demoHoverLength ??= Settings.Edge == Models.DockEdge.Bottom ? _panel.ActualWidth : _panel.ActualHeight;
        var panelPoint = ToPanelPoint(along * _demoHoverLength.Value);
        OnPointerMoved(panelPoint, _panel.TranslatePoint(panelPoint, Root), leftPressed: false);
    }

    /// <summary>Comprimento dos ícones antes da ampliação, para a fração não andar junto com ela.</summary>
    private double? _demoHoverLength;

    internal void SaveDemoDock(string name) => Graphics.DemoCapture.Save(name, Glass);

    private Point ToPanelPoint(double along)
    {
        var cross = _panel is null ? 0 : (Settings.Edge == Models.DockEdge.Bottom ? _panel.ActualHeight : _panel.ActualWidth) / 2;
        return Settings.Edge == Models.DockEdge.Bottom ? new Point(along, cross) : new Point(cross, along);
    }

    /// <summary>Demonstração (Debug): devolve o painel ao mouse e fotografa o recolhimento, se acontecer.</summary>
    internal void DemoReleasePreview()
    {
        _preview.IsDemo = false;
        _preview.CaptureNextCollapse = true;
    }
}
#endif
