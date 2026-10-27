using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
/// As miniaturas são do DWM: ele as compõe por cima do popup, nos retângulos dos marcadores do
/// modelo. Elas não seguem as transformações do WPF, então durante as animações de abrir e fechar
/// cada quadro reposiciona e esmaece as miniaturas junto com o vidro.
/// </remarks>
public partial class WindowPreview : Popup
{
    /// <summary>Altura das miniaturas com a dock embaixo; nas laterais, a largura é fixa.</summary>
    private const double BottomThumbnailHeight = 112;
    private const double SideThumbnailWidth = 200;
    private const double MinThumbnailWidth = 96;
    private const double MaxThumbnailWidth = 208;
    private const double MinThumbnailHeight = 64;
    private const double MaxThumbnailHeight = 140;

    private readonly DockViewModel _dock;
    private readonly DispatcherTimer _hoverWatcher = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private List<WindowPreviewItem> _items = [];
    private DockItemViewModel? _item;
    private DockItemView? _anchor;
    private DockEdge _edge;
    private nint _probe;
    private DateTime _trackUntil;
    private bool _isTracking;
    private bool _isClosing;
    private DateTime? _cursorLeftAt;

    public WindowPreview(DockViewModel dock)
    {
        InitializeComponent();
        _dock = dock;
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
        Cards.ItemsPanel = (ItemsPanelTemplate)FindResource(edge == DockEdge.Bottom ? "HorizontalCards" : "VerticalCards");
        Rebuild();
        if (_items.Count == 0)
        {
            Detach();
            return;
        }

        anchor.IsPreviewOpen = true;
        PlacementTarget = anchor;
        Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        ScreenBackdrop.Shared.CaptureBeside(anchor, edge, Child.DesiredSize);
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
        CaptureCollapse();
    }

    private static WindowPreviewItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as WindowPreviewItem;

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
        foreach (var item in _items)
        {
            Dwmapi.UnregisterThumbnail(item.Thumbnail);
        }

        _items = [];
        Cards.ItemsSource = null;
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

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DockItemViewModel.Windows) || !IsOpen)
        {
            return;
        }

        Rebuild();
        if (_items.Count == 0)
        {
            IsOpen = false;
        }
    }

    /// <summary>
    /// Um cartão por janela. Janelas que continuam abertas mantêm o lugar e a miniatura já
    /// registrada; as novas entram no fim, para os cartões não trocarem de posição sob o mouse.
    /// </summary>
    private void Rebuild()
    {
        if (_item is null)
        {
            return;
        }

        var foreground = User32.GetForegroundWindow();
        var windows = _item.Windows.ToDictionary(window => window.Handle);
        var items = new List<WindowPreviewItem>();
        foreach (var previous in _items)
        {
            if (windows.Remove(previous.Window.Handle, out var window))
            {
                items.Add(CreateItem(window, previous.SourceSize, foreground, previous.Thumbnail));
            }
            else
            {
                Dwmapi.UnregisterThumbnail(previous.Thumbnail);
            }
        }

        foreach (var window in _item.Windows.Where(window => windows.ContainsKey(window.Handle)))
        {
            items.Add(CreateItem(window, MeasureSource(window.Handle), foreground, thumbnail: 0));
        }

        _items = items;
        Cards.ItemsSource = items;
    }

    private WindowPreviewItem CreateItem(AppWindow window, Size source, nint foreground, nint thumbnail)
    {
        var aspect = source.Width / source.Height;
        var (width, height) = _edge == DockEdge.Bottom
            ? (Math.Clamp(BottomThumbnailHeight * aspect, MinThumbnailWidth, MaxThumbnailWidth), BottomThumbnailHeight)
            : (SideThumbnailWidth, Math.Clamp(SideThumbnailWidth / aspect, MinThumbnailHeight, MaxThumbnailHeight));

        return new WindowPreviewItem(window, _item?.Icon, width, height, window.Handle == foreground, source) { Thumbnail = thumbnail };
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

        foreach (var item in _items.Where(item => item.Thumbnail == 0))
        {
            item.Thumbnail = Dwmapi.RegisterThumbnail(source.Handle, item.Window.Handle);
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
        foreach (var item in _items)
        {
            if (item.Thumbnail == 0 || item.Placeholder is not { IsLoaded: true } placeholder
                || PresentationSource.FromVisual(placeholder) is not HwndSource { RootVisual: { } root } source)
            {
                continue;
            }

            var bounds = placeholder.TransformToAncestor(root).TransformBounds(new Rect(placeholder.RenderSize));
            bounds.Transform(source.CompositionTarget.TransformToDevice);
            Dwmapi.UpdateThumbnail(item.Thumbnail, FitInside(bounds, item.SourceSize), opacity, visible: true);
        }
    }

    /// <summary>Acompanha o vidro quadro a quadro enquanto ele anima.</summary>
    private void Track(TimeSpan duration)
    {
        _trackUntil = DateTime.Now + duration;
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
        if (DateTime.Now > _trackUntil)
        {
            StopTracking();
        }
    }

    /// <summary>Demonstração (Debug): o painel fica aberto sem o mouse por perto.</summary>
    internal bool IsDemo { get; set; }

    /// <summary>Demonstração (Debug): fotografa o próximo recolhimento no meio do caminho.</summary>
    internal bool CaptureNextCollapse { get; set; }

    /// <summary>Demonstração (Debug): fotografa a tela em volta do painel e do ícone.</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    internal void SaveDemoFrame(string name)
    {
        if (IsOpen)
        {
            Graphics.DemoCapture.Save(name, PopupContent, _anchor);
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void CaptureCollapse()
    {
        if (!CaptureNextCollapse)
        {
            return;
        }

        CaptureNextCollapse = false;
        var timer = new DispatcherTimer { Interval = DockPopupAnimation.CloseDuration * 0.8 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SaveDemoFrame("preview-collapsing");
        };
        timer.Start();
    }

    private void CloseWhenCursorLeaves()
    {
        if (IsDemo || DockPopupPlacement.IsCursorOver(PopupContent) || DockPopupPlacement.IsCursorOver(_anchor))
        {
            _cursorLeftAt = null;
            return;
        }

        _cursorLeftAt ??= DateTime.Now;
        if (DateTime.Now - _cursorLeftAt >= DockPopupAnimation.HoverGrace)
        {
            CloseAnimated();
        }
    }

    private void OnThumbnailLoaded(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            item.Placeholder = (FrameworkElement)sender;
            RegisterThumbnails();
        }
    }

    private void OnCardLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemOf(sender) is { } preview && _item is { } item)
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
}

/// <summary>Cartão de uma janela no painel de miniaturas.</summary>
public sealed class WindowPreviewItem(AppWindow window, ImageSource? icon, double width, double height, bool isActive, Size sourceSize)
{
    public AppWindow Window => window;

    public string Title => window.Title;

    public ImageSource? Icon => icon;

    public double ThumbnailWidth => width;

    public double ThumbnailHeight => height;

    /// <summary>A janela em primeiro plano ganha um contorno.</summary>
    public bool IsActive => isActive;

    /// <summary>Tamanho da janela de origem, em pixels.</summary>
    internal Size SourceSize => sourceSize;

    internal nint Thumbnail { get; set; }

    internal FrameworkElement? Placeholder { get; set; }
}
