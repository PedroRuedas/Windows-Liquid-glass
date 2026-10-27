using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using LiquidGlass.Services;
using ManagedShell.WindowsTray;
using NativeRect = ManagedShell.Interop.NativeMethods.Rect;

namespace LiquidGlass.Views;

/// <summary>
/// Painel de vidro com os ícones da bandeja do sistema. Os eventos do mouse são repassados ao app
/// dono de cada ícone, que então mostra o próprio menu ou janela, como na bandeja do Windows.
/// </summary>
public partial class TrayFlyout : Popup
{
    private const string Hint = "Clique para abrir · botão direito para mais opções";

    /// <summary>
    /// Enquanto o painel está aberto, um timer leve acompanha o cursor: ele fecha quando o mouse
    /// sai do painel e da setinha. Cliques fora não fecham o painel.
    /// </summary>
    private readonly DispatcherTimer _hoverWatcher = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private DockEdge _edge;
    private bool _isClosing;
    /// <summary>Quando o cursor saiu do painel e da setinha (<see cref="Stopwatch"/>, imune a acertos do relógio).</summary>
    private long? _cursorLeftAt;
    private INotifyCollectionChanged? _observedIcons;

    public TrayFlyout()
    {
        InitializeComponent();
        CustomPopupPlacementCallback = (popup, target, _) =>
            DockPopupPlacement.Beside(_edge, popup, target, DockPopupPlacement.PanelGap);
        _hoverWatcher.Tick += (_, _) => CloseWhenCursorLeaves();
        Opened += (_, _) =>
        {
            _cursorLeftAt = null;
            DockPopupAnimation.Open(this, PopupContent, _edge);
            _hoverWatcher.Start();
        };
        Closed += (_, _) =>
        {
            _hoverWatcher.Stop();
            _isClosing = false;
        };
    }

    /// <summary>Abre o painel junto ao <paramref name="anchor"/> ou o fecha se já estiver aberto.</summary>
    public void Toggle(FrameworkElement anchor, DockEdge edge, ICollectionView? icons)
    {
        if (IsOpen)
        {
            CloseAnimated();
            return;
        }

        _edge = edge;
        PlacementTarget = anchor;
        SetIcons(icons);
        Caption.Text = Hint;

        // O vidro precisa do que está atrás do painel antes de ele aparecer na tela.
        ScreenBackdrop.Shared.CaptureBeside(anchor, edge);
        IsOpen = true;
    }

    private static uint CursorParameter()
    {
        User32.GetCursorPos(out var cursor);
        return (uint)((cursor.Y << 16) | (cursor.X & 0xFFFF));
    }

    private static NotifyIcon? IconOf(object sender) => (sender as FrameworkElement)?.DataContext as NotifyIcon;

    /// <summary>Títulos da bandeja podem ter várias linhas ("Bluetooth\nConectado"); o rodapé mostra uma.</summary>
    private static string SingleLine(string? title)
    {
        var lines = (title ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? Hint : string.Join(" · ", lines);
    }

    /// <summary>Fecha encolhendo o painel de volta para a dock.</summary>
    public void CloseAnimated()
    {
        if (!IsOpen || _isClosing)
        {
            return;
        }

        _isClosing = true;
        _hoverWatcher.Stop();

        DockPopupAnimation.Collapse(PopupContent, _edge, () => IsOpen = false);
    }

    private void CloseWhenCursorLeaves()
    {
        if (DockPopupPlacement.IsCursorOver(PopupContent) || DockPopupPlacement.IsCursorOver(PlacementTarget as FrameworkElement))
        {
            _cursorLeftAt = null;
            return;
        }

        _cursorLeftAt ??= Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_cursorLeftAt.Value) >= DockPopupAnimation.HoverGrace)
        {
            CloseAnimated();
        }
    }

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

        UpdateCount();
    }

    private void OnIconsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        var count = Icons.Items.Count;
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = count switch
        {
            0 => string.Empty,
            1 => "1 app",
            _ => string.Create(CultureInfo.CurrentCulture, $"{count} apps"),
        };
    }

    private void OnIconMouseEnter(object sender, MouseEventArgs e)
    {
        if (IconOf(sender) is not { } icon || sender is not FrameworkElement tile)
        {
            return;
        }

        Caption.Text = SingleLine(icon.Title);

        // Alguns apps posicionam a própria janela pelo retângulo do ícone (em pixels físicos).
        var topLeft = tile.PointToScreen(new Point(0, 0));
        var bottomRight = tile.PointToScreen(new Point(tile.ActualWidth, tile.ActualHeight));
        icon.Placement = new NativeRect((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
        icon.IconMouseEnter(CursorParameter());
    }

    private void OnIconMouseLeave(object sender, MouseEventArgs e)
    {
        Caption.Text = Hint;
        IconOf(sender)?.IconMouseLeave(CursorParameter());
    }

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
