using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    private static readonly TimeSpan CloseDuration = TimeSpan.FromSeconds(0.16);

    /// <summary>
    /// A dock nunca recebe foco, então o "fechar ao clicar fora" do WPF não é confiável aqui.
    /// Enquanto o painel está aberto, um timer leve observa os botões do mouse.
    /// </summary>
    private readonly DispatcherTimer _outsideClickWatcher = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private DockEdge _edge;
    private bool _isClosing;
    private INotifyCollectionChanged? _observedIcons;

    public TrayFlyout()
    {
        InitializeComponent();
        CustomPopupPlacementCallback = (popup, target, _) => DockPopupPlacement.Beside(_edge, popup, target);
        _outsideClickWatcher.Tick += (_, _) => CloseOnOutsideClick();
        Opened += (_, _) =>
        {
            PlayOpenAnimation();
            _outsideClickWatcher.Start();
        };
        Closed += (_, _) =>
        {
            _outsideClickWatcher.Stop();
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
        ScreenBackdrop.Shared.CaptureAround(anchor);
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
        _outsideClickWatcher.Stop();

        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, CloseDuration) { EasingFunction = easing };
        var shrink = new DoubleAnimation(0.9, CloseDuration) { EasingFunction = easing };
        fade.Completed += (_, _) => IsOpen = false;

        var scale = (ScaleTransform)Panel.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        Panel.BeginAnimation(OpacityProperty, fade);
    }

    private static bool IsCursorOver(FrameworkElement? element)
    {
        if (element is null || PresentationSource.FromVisual(element) is null)
        {
            return false;
        }

        User32.GetCursorPos(out var cursor);
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new Rect(topLeft, bottomRight).Contains(cursor.X, cursor.Y);
    }

    private void CloseOnOutsideClick()
    {
        var anyButtonDown = User32.IsKeyDown(User32.VkLeftButton)
            || User32.IsKeyDown(User32.VkRightButton)
            || User32.IsKeyDown(User32.VkMiddleButton);

        // Cliques na setinha da dock ficam com o Toggle, que fecha o painel do mesmo jeito.
        if (anyButtonDown && !IsCursorOver(Panel) && !IsCursorOver(PlacementTarget as FrameworkElement))
        {
            CloseAnimated();
        }
    }

    private void PlayOpenAnimation()
    {
        Panel.RenderTransformOrigin = _edge switch
        {
            DockEdge.Left => new Point(0, 0.5),
            DockEdge.Right => new Point(1, 0.5),
            _ => new Point(0.5, 1),
        };

        // Remove o estado final de um fechamento animado anterior antes de animar a entrada.
        var scale = (ScaleTransform)Panel.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Panel.BeginAnimation(OpacityProperty, null);

        var storyboard = (Storyboard)FindResource("PopupOpenStoryboard");
        storyboard.Begin(Panel);
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
