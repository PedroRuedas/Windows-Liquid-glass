using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LiquidGlass.Controls;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using LiquidGlass.Services;
using LiquidGlass.ViewModels;

namespace LiquidGlass.Views;

/// <summary>
/// Janela transparente que ocupa a borda da tela e desenha a dock. Só a superfície de vidro e os
/// ícones recebem cliques; o restante da janela deixa o mouse passar.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "A app bar é liberada em Closed, o fim do ciclo de vida da janela.")]
public partial class DockWindow : Window
{
    private const double ShadowPadding = 18;
    private const double LabelSpace = 52;
    private const double SideLabelSpace = 240;
    private const int SpiSetDesktopWallpaper = 0x0014;
    private const int SpiSetWorkArea = 0x002F;

    private static readonly TimeSpan WallpaperSettleDelay = TimeSpan.FromMilliseconds(600);

    private readonly DockViewModel _viewModel;
    private readonly WallpaperService _wallpaper;
    private readonly TrayIconService _tray;
    private readonly TrayFlyout _trayFlyout = new();
    private HwndSource? _source;
    private AppBarManager? _appBar;
    private AutoHideController? _autoHide;
    private ContextMenu? _openMenu;
    private MagnifyingPanel? _panel;
    private Rect _contentBounds = Rect.Empty;
    private bool _isPointerInside;

    public DockWindow(DockViewModel viewModel, WallpaperService wallpaper, TrayIconService tray)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _wallpaper = wallpaper;
        _tray = tray;
        DataContext = viewModel;

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        DragOver += OnDragOver;
        Drop += OnDrop;
        Settings.PropertyChanged += OnSettingsChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.TrayToggleRequested += OnTrayToggleRequested;
        _wallpaper.Changed += OnWallpaperChanged;
    }

    /// <summary>Fonte nativa da janela, usada para receber mensagens do shell.</summary>
    public HwndSource? NativeSource => _source;

    private DockSettings Settings => _viewModel.Settings;

    private double GlassPadding => Math.Max(7, Settings.IconSize * 0.16);

    /// <summary>Espessura do vidro somada à margem da borda: o espaço que a dock reserva na tela.</summary>
    private double DockThickness => Settings.EdgeMargin + Settings.IconSize + (GlassPadding * 2);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(this);
        _source.AddHook(WndProc);

        // Fora do Alt+Tab e sem roubar o foco do app que o usuário está usando.
        User32.AddExtendedStyle(_source.Handle, User32.WsExToolWindow | User32.WsExNoActivate);

        _appBar = new AppBarManager(_source);
        _appBar.PositionChanged += (_, _) => UpdatePlacement();
        _appBar.FullScreenChanged += OnFullScreenChanged;
        _autoHide = new AutoHideController(Root, IsCursorAtDockEdge, ShouldStayVisible);

        UpdatePlacement();
        ApplyGlassSettings();
        _ = _wallpaper.RefreshAsync(Settings.Blur);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.TrayToggleRequested -= OnTrayToggleRequested;
        _wallpaper.Changed -= OnWallpaperChanged;
        _trayFlyout.IsOpen = false;
        _source?.RemoveHook(WndProc);
        _autoHide?.Stop();
        _appBar?.Dispose();
    }

    /// <summary>
    /// Reserva o espaço da dock na borda escolhida e posiciona a janela, acima da barra de tarefas
    /// nativa quando ela continua visível.
    /// </summary>
    private void UpdatePlacement()
    {
        if (_source is null || _appBar is null || _autoHide is null)
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var reserved = Settings.AutoHide ? 0 : (int)Math.Ceiling(DockThickness * scale);
        _appBar.Reserve(Settings.Edge, reserved);
        var area = _appBar.Bounds;
        var labelSpace = Settings.Edge == DockEdge.Bottom ? LabelSpace : SideLabelSpace;
        var thickness = Settings.EdgeMargin + GlassPadding + (Settings.IconSize * Math.Max(_viewModel.EffectiveMagnification, 1.5)) + labelSpace;
        var size = (int)Math.Ceiling(thickness * scale);

        var (x, y, width, height) = Settings.Edge switch
        {
            DockEdge.Left => (area.Left, area.Top, size, area.Height),
            DockEdge.Right => (area.Right - size, area.Top, size, area.Height),
            _ => (area.Left, area.Bottom - size, area.Width, size),
        };

        User32.SetWindowPos(_source.Handle, User32.HwndTopMost, x, y, width, height, User32.SwpNoActivate);

        var inset = Settings.EdgeMargin + GlassPadding;
        DockItems.Margin = Settings.Edge switch
        {
            DockEdge.Left => new Thickness(inset, 0, 0, 0),
            DockEdge.Right => new Thickness(0, 0, inset, 0),
            _ => new Thickness(0, 0, 0, inset),
        };

        _autoHide.Edge = Settings.Edge;
        _autoHide.HiddenDistance = DockThickness + ShadowPadding;
        _autoHide.AnimationSpeed = Settings.AnimationSpeed;
        _autoHide.IsEnabled = Settings.AutoHide;
    }

    /// <summary>Com a dock escondida, o cursor encostado na borda (na altura da dock) a traz de volta.</summary>
    private bool IsCursorAtDockEdge()
    {
        if (_source is null || !User32.GetCursorPos(out var cursor) || !User32.GetWindowRect(_source.Handle, out var window))
        {
            return false;
        }

        const int Tolerance = 2;
        var glass = Glass.IsLoaded && Glass.ActualWidth > 0
            ? new Rect(Glass.PointToScreen(new Point(0, 0)), Glass.PointToScreen(new Point(Glass.ActualWidth, Glass.ActualHeight)))
            : new Rect(window.Left, window.Top, window.Width, window.Height);

        return Settings.Edge switch
        {
            DockEdge.Left => cursor.X <= window.Left + Tolerance && cursor.Y >= glass.Top && cursor.Y <= glass.Bottom,
            DockEdge.Right => cursor.X >= window.Right - 1 - Tolerance && cursor.Y >= glass.Top && cursor.Y <= glass.Bottom,
            _ => cursor.Y >= window.Bottom - 1 - Tolerance && cursor.X >= glass.Left && cursor.X <= glass.Right,
        };
    }

    private bool ShouldStayVisible() => Root.IsMouseOver || _openMenu is { IsOpen: true } || _trayFlyout.IsOpen;

    private void OnFullScreenChanged(object? sender, bool isFullScreen)
    {
        // Jogos e vídeos em tela cheia não devem ficar com a dock por cima.
        Visibility = isFullScreen ? Visibility.Hidden : Visibility.Visible;
    }

    private void ApplyGlassSettings()
    {
        var tint = Settings.TintColor;
        Glass.ShadowPadding = ShadowPadding;
        Glass.Refraction = Settings.Refraction * 36;
        Glass.ChromaticAberration = Settings.ChromaticAberration;
        Glass.Highlight = Settings.Highlight;
        Glass.Saturation = Settings.Saturation;
        Glass.Shadow = Settings.Shadow;
        Glass.Tint = Color.FromArgb((byte)Math.Round(Math.Clamp(Settings.TintStrength, 0, 1) * 255), tint.R, tint.G, tint.B);

        var light = Settings.Edge switch
        {
            DockEdge.Left => new Vector(1, -0.4),
            DockEdge.Right => new Vector(-1, -0.4),
            _ => new Vector(-0.3, -1),
        };
        light.Normalize();
        Glass.LightDirection = light;
    }

    /// <summary>Ajusta o vidro e a área de clique ao redor dos ícones.</summary>
    private void LayoutGlass()
    {
        if (_contentBounds.IsEmpty)
        {
            return;
        }

        var glass = Rect.Inflate(ExtendToDockLength(_contentBounds), GlassPadding, GlassPadding);
        var thickness = Math.Min(glass.Width, glass.Height);
        Glass.CornerRadius = Math.Clamp(Settings.Roundness, 0, 1) * thickness / 2;
        Glass.BezelWidth = thickness * 0.5;
        Place(Glass, Rect.Inflate(glass, ShadowPadding, ShadowPadding));

        var hitArea = glass;
        if (_isPointerInside)
        {
            var reach = Settings.IconSize * (_viewModel.EffectiveMagnification - 1);
            hitArea = Settings.Edge switch
            {
                DockEdge.Left => new Rect(hitArea.X, hitArea.Y, hitArea.Width + reach, hitArea.Height),
                DockEdge.Right => new Rect(hitArea.X - reach, hitArea.Y, hitArea.Width + reach, hitArea.Height),
                _ => new Rect(hitArea.X, hitArea.Y - reach, hitArea.Width, hitArea.Height + reach),
            };
        }

        Place(HitArea, hitArea);
    }

    /// <summary>
    /// Estende a área do vidro, centralizada na tela, até o comprimento escolhido. Os ícones
    /// continuam no centro; com a ampliação, o vidro ainda cresce se eles passarem do limite.
    /// </summary>
    private Rect ExtendToDockLength(Rect content)
    {
        var isVertical = Settings.Edge != DockEdge.Bottom;
        var available = isVertical ? GlassLayer.ActualHeight : GlassLayer.ActualWidth;
        var length = (Math.Clamp(Settings.DockLength, 0, 1) * available) - (GlassPadding * 2);
        if (length <= (isVertical ? content.Height : content.Width))
        {
            return content;
        }

        var start = (available - length) / 2;
        var extended = isVertical
            ? new Rect(content.X, start, content.Width, length)
            : new Rect(start, content.Y, length, content.Height);
        return Rect.Union(content, extended);
    }

    private static void Place(FrameworkElement element, Rect bounds)
    {
        Canvas.SetLeft(element, bounds.X);
        Canvas.SetTop(element, bounds.Y);
        element.Width = Math.Max(bounds.Width, 0);
        element.Height = Math.Max(bounds.Height, 0);
    }

    private void OnContentBoundsChanged(object sender, ContentBoundsChangedEventArgs e)
    {
        if (e.OriginalSource is not MagnifyingPanel panel)
        {
            return;
        }

        _panel = panel;
        _contentBounds = panel.TransformToVisual(GlassLayer).TransformBounds(e.Bounds);
        LayoutGlass();
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        if (_panel is null)
        {
            return;
        }

        var position = e.GetPosition(_panel);
        _panel.CursorPosition = Settings.Edge == DockEdge.Bottom ? position.X : position.Y;

        if (!_isPointerInside)
        {
            _isPointerInside = true;
            LayoutGlass();
        }
    }

    private void OnRootMouseLeave(object sender, MouseEventArgs e)
    {
        if (_panel is not null)
        {
            _panel.CursorPosition = double.NaN;
        }

        _isPointerInside = false;
        LayoutGlass();
    }

    private void OnItemContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is DockItemView { ContextMenu: { } menu, DataContext: DockItemViewModel item })
        {
            DockMenus.Populate(menu, item, _viewModel, (UIElement)sender);
            _openMenu = menu;
        }
    }

    private void OnGlassContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Glass.ContextMenu is { } menu)
        {
            DockMenus.Populate(menu, null, _viewModel);
            _openMenu = menu;
        }
    }

    private void OnTrayToggleRequested(object? sender, DockItemViewModel item)
    {
        if (DockItems.ItemContainerGenerator.ContainerFromItem(item) is UIElement anchor)
        {
            _trayFlyout.Toggle(anchor, Settings.Edge, _tray.Icons);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
            {
                _viewModel.PinPath(path);
            }
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DockSettings.Edge):
                UpdatePlacement();
                ApplyGlassSettings();
                break;
            case nameof(DockSettings.IconSize):
            case nameof(DockSettings.EdgeMargin):
            case nameof(DockSettings.TaskbarMode):
            case nameof(DockSettings.AutoHide):
            case nameof(DockSettings.AnimationSpeed):
                UpdatePlacement();
                break;
            case nameof(DockSettings.Roundness):
            case nameof(DockSettings.DockLength):
                LayoutGlass();
                break;
            case nameof(DockSettings.Blur):
                _ = _wallpaper.SetBlurAsync(Settings.Blur);
                break;
            case nameof(DockSettings.Refraction):
            case nameof(DockSettings.ChromaticAberration):
            case nameof(DockSettings.Highlight):
            case nameof(DockSettings.Saturation):
            case nameof(DockSettings.Shadow):
            case nameof(DockSettings.TintColor):
            case nameof(DockSettings.TintStrength):
                ApplyGlassSettings();
                break;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DockViewModel.EffectiveMagnification))
        {
            UpdatePlacement();
        }
    }

    private void OnWallpaperChanged(object? sender, EventArgs e)
    {
        Glass.BackdropOrigin = _wallpaper.MonitorOrigin;
        Glass.Backdrop = _wallpaper.Backdrop;
    }

    private async void RefreshWallpaperLater()
    {
        // O Windows anima a troca de papel de parede; espera a imagem final antes de capturar.
        await Task.Delay(WallpaperSettleDelay);
        await _wallpaper.RefreshAsync(Settings.Blur);
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case User32.WmSettingChange when wParam == SpiSetDesktopWallpaper:
                RefreshWallpaperLater();
                break;
            case User32.WmSettingChange when wParam == SpiSetWorkArea:
                Dispatcher.BeginInvoke(UpdatePlacement);
                break;
            case User32.WmDisplayChange:
            case User32.WmDpiChanged:
                Dispatcher.BeginInvoke(UpdatePlacement);
                RefreshWallpaperLater();
                break;
        }

        return 0;
    }
}
