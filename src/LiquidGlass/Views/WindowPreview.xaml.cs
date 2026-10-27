using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LiquidGlass.Controls;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using LiquidGlass.Services;
using LiquidGlass.ViewModels;

namespace LiquidGlass.Views;

/// <summary>
/// Miniaturas vivas das janelas de um app, abertas ao parar o mouse sobre o ícone dele, como na
/// barra de tarefas do Windows. Clicar numa miniatura traz a janela; o X (ou o botão do meio) a fecha.
/// </summary>
/// <remarks>
/// As miniaturas são do DWM: ele as compõe por cima do popup, nos retângulos dos marcadores dos
/// cartões. Elas não seguem as transformações do WPF, então durante as animações de abrir e fechar
/// cada quadro reposiciona e esmaece as miniaturas junto com o vidro.
/// </remarks>
public partial class WindowPreview : Popup
{
    /// <summary>
    /// Em volta dos cartões: o espaçamento da sombra e a margem interna (WindowPreview.xaml), mais
    /// uma folga até a borda da tela.
    /// </summary>
    private const double PanelChrome = (2 * (14 + 8)) + 16;

    /// <summary>Altura da linha "Mais N janelas", reservada para longe da dock.</summary>
    private const double MoreWindowsHeight = 24;

    /// <summary>Arredondamento de pixels do WrapPanel, para ele nunca quebrar a linha antes da conta.</summary>
    private const double WrapSlack = 12;

    private readonly DockViewModel _dock;
    private readonly DispatcherTimer _hoverWatcher = new() { Interval = TimeSpan.FromMilliseconds(40) };

    /// <summary>Os cartões na tela, na ordem em que aparecem.</summary>
    private readonly ObservableCollection<WindowPreviewItem> _cards = [];

    /// <summary>Miniatura do DWM e marcador de cada janela do app, pelo identificador da janela.</summary>
    private readonly Dictionary<nint, ThumbnailSlot> _slots = [];

    private DockItemViewModel? _item;
    private DockItemView? _anchor;
    private DockEdge _edge;
    private nint _probe;
    private Size _available;

    /// <summary>Tamanho do painel quando o fundo de vidro foi fotografado; maior que isso, o fundo não cobre.</summary>
    private Size _capturedSize;
    private long _trackStart;
    private TimeSpan _trackDuration;
    private bool _isTracking;
    private bool _isClosing;
    /// <summary>Quando o cursor saiu do painel e do ícone (<see cref="Stopwatch"/>, imune a acertos do relógio).</summary>
    private long? _cursorLeftAt;
    private WindowPreviewItem? _pressedCard;

    public WindowPreview(DockViewModel dock)
    {
        InitializeComponent();
        _dock = dock;
        Cards.ItemsSource = _cards;
        CustomPopupPlacementCallback = (popup, target, _) => DockPopupPlacement.Beside(_edge, popup, target);
        _hoverWatcher.Tick += (_, _) => CloseWhenCursorLeaves();
        Opened += OnOpened;
        Closed += OnClosed;
        PopupContent.SizeChanged += (_, _) => UpdateThumbnails();
    }

    /// <summary>
    /// Mostra as janelas de <paramref name="item"/> junto ao ícone. Com o painel aberto em outro
    /// ícone, ele troca na hora. <paramref name="probe"/> é uma janela de nível superior qualquer
    /// do processo, usada para medir as janelas antes de o painel existir.
    /// </summary>
    public void Show(DockItemView anchor, DockItemViewModel item, DockEdge edge, nint probe)
    {
        if (IsOpen && !_isClosing && item == _item && anchor == _anchor)
        {
            return;
        }

        // O fundo de vidro é um retrato da tela: precisa ser tirado sem o painel antigo na frente.
        IsOpen = false;

        _item = item;
        _anchor = anchor;
        _edge = edge;
        _probe = probe;
        _item.PropertyChanged += OnItemPropertyChanged;
        _available = AvailableArea(anchor, edge);
        Cards.ItemsPanel = (ItemsPanelTemplate)FindResource(edge == DockEdge.Bottom ? "HorizontalCards" : "VerticalCards");
        Cards.MaxWidth = edge == DockEdge.Bottom ? _available.Width : double.PositiveInfinity;
        Cards.MaxHeight = edge == DockEdge.Bottom ? double.PositiveInfinity : _available.Height;
        Rebuild();
        if (_cards.Count == 0)
        {
            Detach();
            return;
        }

        anchor.IsPreviewOpen = true;
        PlacementTarget = anchor;
        _capturedSize = MeasurePanel();
        ScreenBackdrop.Shared.CaptureBeside(anchor, edge, _capturedSize);
        IsOpen = true;
    }

    /// <summary>Fecha recolhendo para dentro do ícone, como o painel de segundo plano.</summary>
    public void CloseAnimated()
    {
        if (!IsOpen || _isClosing)
        {
            return;
        }

        _isClosing = true;
        _hoverWatcher.Stop();

        DockPopupAnimation.Collapse(PopupContent, _edge, () =>
        {
            if (_isClosing)
            {
                IsOpen = false;
            }
        });

        // As miniaturas não encolhem com o vidro sozinhas: acompanham quadro a quadro.
        Track(DockPopupAnimation.CloseDuration);
#if DEBUG
        CaptureCollapse();
#endif
    }

    private static WindowPreviewItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as WindowPreviewItem;

    /// <summary>
    /// Espaço para os cartões, em DIPs: a área de trabalho inteira ao longo da dock e, para longe
    /// dela, do ícone até a borda oposta.
    /// </summary>
    private static Size AvailableArea(FrameworkElement anchor, DockEdge edge)
    {
        var work = User32.GetPrimaryMonitorInfo().WorkArea;
        var icon = new Rect(anchor.PointToScreen(new Point(0, 0)), anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight)));
        (double Along, double Across) reach = edge switch
        {
            DockEdge.Left => (work.Bottom - work.Top, work.Right - icon.Right),
            DockEdge.Right => (work.Bottom - work.Top, icon.Left - work.Left),
            _ => (work.Right - work.Left, icon.Top - work.Top),
        };

        var scale = VisualTreeHelper.GetDpi(anchor).DpiScaleX;
        var along = Math.Max((reach.Along / scale) - PanelChrome, 0);
        var across = Math.Max((reach.Across / scale) - PanelChrome - MoreWindowsHeight, 0);
        return edge == DockEdge.Bottom ? new Size(along, across) : new Size(across, along);
    }

    /// <summary>Encaixa a janela (com a proporção dela) no retângulo do marcador, centralizada.</summary>
    private static User32.Rect FitInside(Rect bounds, Size source)
    {
        var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        var left = bounds.X + ((bounds.Width - width) / 2);
        var top = bounds.Y + ((bounds.Height - height) / 2);
        return new User32.Rect
        {
            Left = (int)Math.Round(left),
            Top = (int)Math.Round(top),
            Right = (int)Math.Round(left + width),
            Bottom = (int)Math.Round(top + height),
        };
    }

    private Size MeasurePanel()
    {
        Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Child.DesiredSize;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _cursorLeftAt = null;
        _isClosing = false;
        DockPopupAnimation.Open(this, PopupContent, _edge);
        _hoverWatcher.Start();
        RegisterThumbnails();
        Track(DockPopupAnimation.OpenDuration);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hoverWatcher.Stop();
        StopTracking();
        _isClosing = false;
        foreach (var slot in _slots.Values)
        {
            slot.Release();
        }

        _slots.Clear();
        _cards.Clear();
        Detach();
    }

    private void Detach()
    {
        if (_item is not null)
        {
            _item.PropertyChanged -= OnItemPropertyChanged;
            _item = null;
        }

        if (_anchor is not null)
        {
            _anchor.IsPreviewOpen = false;
            _anchor = null;
        }
    }

    /// <summary>Janela aberta, fechada ou com título novo enquanto o painel está na tela.</summary>
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DockItemViewModel.Windows) || !IsOpen || _isClosing)
        {
            return;
        }

        Rebuild();
        if (_cards.Count == 0)
        {
            IsOpen = false;
            return;
        }

        // Painel maior que o retrato do fundo: abre de novo, com um retrato do tamanho certo.
        var size = MeasurePanel();
        if ((size.Width > _capturedSize.Width + 1 || size.Height > _capturedSize.Height + 1) && _anchor is { } anchor && _item is { } item)
        {
            IsOpen = false;
            Show(anchor, item, _edge, _probe);
            return;
        }

        // Cartões que mudaram de lugar levam a miniatura junto, depois do novo layout.
        Dispatcher.BeginInvoke(UpdateThumbnails, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Um cartão por janela. Janelas que continuam abertas mantêm o lugar, o cartão e a miniatura
    /// já registrada; as novas entram no fim, para os cartões não trocarem de posição sob o mouse.
    /// </summary>
    private void Rebuild()
    {
        if (_item is null)
        {
            return;
        }

        var windows = _item.Windows.ToDictionary(window => window.Handle);
        foreach (var closed in _slots.Keys.Where(handle => !windows.ContainsKey(handle)).ToList())
        {
            _slots[closed].Release();
            _slots.Remove(closed);
        }

        var ordered = _cards.Select(card => card.Window.Handle).Where(windows.ContainsKey).ToList();
        ordered.AddRange(_item.Windows.Select(window => window.Handle).Where(handle => !ordered.Contains(handle)));
        foreach (var handle in ordered.Where(handle => !_slots.ContainsKey(handle)))
        {
            _slots[handle] = new ThumbnailSlot(MeasureSource(handle));
        }

        // Com janelas demais para a tela, as que sobram ficam no menu do botão direito.
        var fitArea = _edge == DockEdge.Bottom
            ? new Size(_available.Width - WrapSlack, _available.Height)
            : new Size(_available.Width, _available.Height - WrapSlack);
        var (scale, count) = WindowPreviewLayout.Fit([.. ordered.Select(handle => _slots[handle].Source)], _edge, fitArea);
        var visible = ordered.Take(count).ToList();

        for (var index = _cards.Count - 1; index >= 0; index--)
        {
            var handle = _cards[index].Window.Handle;
            if (!visible.Contains(handle))
            {
                _cards.RemoveAt(index);
                if (_slots.TryGetValue(handle, out var slot))
                {
                    slot.Release();
                }
            }
        }

        var foreground = User32.GetForegroundWindow();
        foreach (var handle in visible)
        {
            var size = WindowPreviewLayout.ThumbnailSize(_slots[handle].Source, _edge, scale);
            if (_cards.FirstOrDefault(card => card.Window.Handle == handle) is not { } card)
            {
                card = new WindowPreviewItem(windows[handle], _item.Icon);
                _cards.Add(card);
            }

            card.Window = windows[handle];
            card.ThumbnailWidth = size.Width;
            card.ThumbnailHeight = size.Height;
            card.IsActive = handle == foreground;
        }

        var hidden = ordered.Count - count;
        MoreWindows.Text = hidden == 1 ? "Mais 1 janela no menu do botão direito" : $"Mais {hidden} janelas no menu do botão direito";
        MoreWindows.Visibility = hidden > 0 && count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Tamanho da janela como o DWM a mostra (de uma minimizada, o da última imagem).</summary>
    private Size MeasureSource(nint hwnd)
    {
        var thumbnail = Dwmapi.RegisterThumbnail(_probe, hwnd);
        var size = Dwmapi.QuerySourceSize(thumbnail);
        Dwmapi.UnregisterThumbnail(thumbnail);
        if (size is { } measured)
        {
            return new Size(measured.Width, measured.Height);
        }

        return Dwmapi.GetVisibleBounds(hwnd) is { Width: > 0, Height: > 0 } bounds ? new Size(bounds.Width, bounds.Height) : new Size(16, 10);
    }

    private void RegisterThumbnails()
    {
        if (PresentationSource.FromVisual(Child) is not HwndSource source)
        {
            return;
        }

        foreach (var card in _cards)
        {
            if (_slots.TryGetValue(card.Window.Handle, out var slot) && slot is { Thumbnail: 0, Placeholder: not null })
            {
                slot.Thumbnail = Dwmapi.RegisterThumbnail(source.Handle, card.Window.Handle);
            }
        }

        UpdateThumbnails();
    }

    /// <summary>Põe cada miniatura sobre o marcador dela, com a opacidade atual do painel.</summary>
    private void UpdateThumbnails()
    {
        if (!IsOpen)
        {
            return;
        }

        var opacity = (byte)Math.Round(Math.Clamp(PopupContent.Opacity, 0, 1) * 255);
        foreach (var slot in _slots.Values)
        {
            if (slot.Thumbnail == 0 || slot.Placeholder is not { IsLoaded: true } placeholder
                || PresentationSource.FromVisual(placeholder) is not HwndSource { RootVisual: { } root } source)
            {
                continue;
            }

            var bounds = placeholder.TransformToAncestor(root).TransformBounds(new Rect(placeholder.RenderSize));
            bounds.Transform(source.CompositionTarget.TransformToDevice);
            Dwmapi.UpdateThumbnail(slot.Thumbnail, FitInside(bounds, slot.Source), opacity, visible: true);
        }
    }

    /// <summary>Acompanha o vidro quadro a quadro enquanto ele anima.</summary>
    private void Track(TimeSpan duration)
    {
        _trackStart = Stopwatch.GetTimestamp();
        _trackDuration = duration;
        if (!_isTracking)
        {
            _isTracking = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void StopTracking()
    {
        if (_isTracking)
        {
            _isTracking = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        UpdateThumbnails();
        if (Stopwatch.GetElapsedTime(_trackStart) > _trackDuration)
        {
            StopTracking();
        }
    }

    private void CloseWhenCursorLeaves()
    {
#if DEBUG
        if (IsDemo)
        {
            _cursorLeftAt = null;
            return;
        }
#endif

        if (DockPopupPlacement.IsCursorOver(PopupContent) || DockPopupPlacement.IsCursorOver(_anchor))
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

    private void OnThumbnailLoaded(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } card && _slots.TryGetValue(card.Window.Handle, out var slot))
        {
            slot.Placeholder = (FrameworkElement)sender;
            RegisterThumbnails();
        }
    }

    private void OnCardLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedCard = ItemOf(sender);
        e.Handled = true;
    }

    /// <summary>Só um clique que começou no mesmo cartão traz a janela; soltar o botão vindo de fora não.</summary>
    private void OnCardLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pressed = _pressedCard;
        _pressedCard = null;
        if (ItemOf(sender) is { } preview && preview == pressed && _item is { } item)
        {
            IsOpen = false;
            _dock.ActivateWindow(item, preview.Window);
        }

        e.Handled = true;
    }

    private void OnCardMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Botão do meio fecha a janela, como nas miniaturas da barra de tarefas.
        if (e.ChangedButton == MouseButton.Middle && ItemOf(sender) is { } preview)
        {
            _dock.CloseWindow(preview.Window);
            e.Handled = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } preview)
        {
            _dock.CloseWindow(preview.Window);
        }
    }

    /// <summary>A miniatura do DWM de uma janela e o retângulo da tela onde ela é desenhada.</summary>
    private sealed class ThumbnailSlot(Size source)
    {
        /// <summary>Tamanho da janela de origem, em pixels.</summary>
        public Size Source => source;

        public nint Thumbnail { get; set; }

        public FrameworkElement? Placeholder { get; set; }

        /// <summary>O cartão saiu da tela: a miniatura deixa de ser desenhada.</summary>
        public void Release()
        {
            Dwmapi.UnregisterThumbnail(Thumbnail);
            Thumbnail = 0;
            Placeholder = null;
        }
    }
}

/// <summary>Cartão de uma janela no painel de miniaturas. Atualiza no lugar quando a janela muda de título.</summary>
public sealed partial class WindowPreviewItem(AppWindow window, ImageSource? icon) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private AppWindow _window = window;

    [ObservableProperty]
    private double _thumbnailWidth;

    [ObservableProperty]
    private double _thumbnailHeight;

    /// <summary>A janela em primeiro plano ganha um contorno.</summary>
    [ObservableProperty]
    private bool _isActive;

    public string Title => Window.Title;

    public ImageSource? Icon => icon;
}
