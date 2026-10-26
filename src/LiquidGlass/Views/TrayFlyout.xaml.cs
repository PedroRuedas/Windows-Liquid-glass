using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using ManagedShell.WindowsTray;
using NativeRect = ManagedShell.Interop.NativeMethods.Rect;

namespace LiquidGlass.Views;

/// <summary>
/// Painel com os ícones da bandeja do sistema. Os eventos do mouse são repassados ao app dono de
/// cada ícone, que então mostra o próprio menu ou janela, como na bandeja do Windows.
/// </summary>
public partial class TrayFlyout : Popup
{
    /// <summary>Clicar no botão da dock com o painel aberto fecha o painel antes do clique chegar.</summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private DockEdge _edge;
    private DateTime _closedAt;
    private INotifyCollectionChanged? _observedIcons;

    public TrayFlyout()
    {
        InitializeComponent();
        CustomPopupPlacementCallback = (popup, target, _) => DockPopupPlacement.Beside(_edge, popup, target);
        Closed += (_, _) => _closedAt = DateTime.Now;
    }

    /// <summary>Abre o painel junto ao <paramref name="anchor"/> ou o fecha se já estiver aberto.</summary>
    public void Toggle(UIElement anchor, DockEdge edge, ICollectionView? icons)
    {
        if (IsOpen || DateTime.Now - _closedAt < ReopenGuard)
        {
            IsOpen = false;
            return;
        }

        _edge = edge;
        PlacementTarget = anchor;
        SetIcons(icons);
        IsOpen = true;
    }

    private static uint CursorParameter()
    {
        User32.GetCursorPos(out var cursor);
        return (uint)((cursor.Y << 16) | (cursor.X & 0xFFFF));
    }

    private static NotifyIcon? IconOf(object sender) => (sender as FrameworkElement)?.DataContext as NotifyIcon;

    private void SetIcons(ICollectionView? icons)
    {
        if (_observedIcons is not null)
        {
            _observedIcons.CollectionChanged -= OnIconsChanged;
        }

        Icons.ItemsSource = icons;
        _observedIcons = icons;
        if (_observedIcons is not null)
        {
            _observedIcons.CollectionChanged += OnIconsChanged;
        }

        UpdateEmptyState();
    }

    private void OnIconsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = Icons.HasItems ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnIconMouseEnter(object sender, MouseEventArgs e)
    {
        if (IconOf(sender) is not { } icon || sender is not FrameworkElement cell)
        {
            return;
        }

        // Alguns apps posicionam a própria janela pelo retângulo do ícone (em pixels físicos).
        var topLeft = cell.PointToScreen(new Point(0, 0));
        var bottomRight = cell.PointToScreen(new Point(cell.ActualWidth, cell.ActualHeight));
        icon.Placement = new NativeRect((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
        icon.IconMouseEnter(CursorParameter());
    }

    private void OnIconMouseLeave(object sender, MouseEventArgs e) => IconOf(sender)?.IconMouseLeave(CursorParameter());

    private void OnIconMouseMove(object sender, MouseEventArgs e) => IconOf(sender)?.IconMouseMove(CursorParameter());

    private void OnIconMouseDown(object sender, MouseButtonEventArgs e)
    {
        IconOf(sender)?.IconMouseDown(e.ChangedButton, CursorParameter(), User32.GetDoubleClickTime());
        e.Handled = true;
    }

    private void OnIconMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (IconOf(sender) is { } icon)
        {
            // Fecha antes de repassar o clique: o menu ou a janela do app aparece por cima da dock.
            IsOpen = false;
            icon.IconMouseUp(e.ChangedButton, CursorParameter(), User32.GetDoubleClickTime());
        }

        e.Handled = true;
    }
}
