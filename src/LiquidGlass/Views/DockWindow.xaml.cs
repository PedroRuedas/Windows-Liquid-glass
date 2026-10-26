using System.ComponentModel;
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
public partial class DockWindow : Window
{
    private const double ShadowPadding = 18;
    private const double LabelSpace = 52;
    private const int SpiSetDesktopWallpaper = 0x0014;
    private const int SpiSetWorkArea = 0x002F;

    private static readonly TimeSpan WallpaperSettleDelay = TimeSpan.FromMilliseconds(600);

    private readonly DockViewModel _viewModel;
    private readonly WallpaperService _wallpaper;
    private HwndSource? _source;
    private MagnifyingPanel? _panel;
    private Rect _contentBounds = Rect.Empty;
    private bool _isPointerInside;

    public DockWindow(DockViewModel viewModel, WallpaperService wallpaper)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _wallpaper = wallpaper;
        DataContext = viewModel;

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        DragOver += OnDragOver;
        Drop += OnDrop;
        Settings.PropertyChanged += OnSettingsChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _wallpaper.Changed += OnWallpaperChanged;
    }

    /// <summary>Fonte nativa da janela, usada para receber mensagens do shell.</summary>
    public HwndSource? NativeSource => _source;

    private DockSettings Settings => _viewModel.Settings;

    private double GlassPadding => Math.Max(7, Settings.IconSize * 0.16);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(this);
        _source.AddHook(WndProc);

        // Fora do Alt+Tab e sem roubar o foco do app que o usuário está usando.
        User32.AddExtendedStyle(_source.Handle, User32.WsExToolWindow | User32.WsExNoActivate);

        UpdatePlacement();
        ApplyGlassSettings();
        _ = _wallpaper.RefreshAsync(Settings.Blur);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _wallpaper.Changed -= OnWallpaperChanged;
        _source?.RemoveHook(WndProc);
    }

    /// <summary>Posiciona a janela na borda escolhida, acima da barra de tarefas nativa se ela estiver visível.</summary>
    private void UpdatePlacement()
    {
        if (_source is null)
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var area = GetAvailableArea();
        var thickness = Settings.EdgeMargin + GlassPadding + (Settings.IconSize * Math.Max(_viewModel.EffectiveMagnification, 1.5)) + LabelSpace;
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
    }

    private User32.Rect GetAvailableArea()
    {
        var area = User32.GetPrimaryMonitorInfo().Monitor;
        if (Settings.TaskbarMode != TaskbarMode.Visible)
        {
            return area;
        }

        var data = Shell32.CreateAppBarData();
        if (Shell32.SHAppBarMessage(Shell32.AbmGetTaskbarPos, ref data) == 0)
        {
            return area;
        }

        var taskbar = data.Rect;
        switch (Settings.Edge)
        {
            case DockEdge.Bottom when data.Edge == Shell32.AbeBottom:
                area.Bottom = taskbar.Top;
                break;
            case DockEdge.Left when data.Edge == Shell32.AbeLeft:
                area.Left = taskbar.Right;
                break;
            case DockEdge.Right when data.Edge == Shell32.AbeRight:
                area.Right = taskbar.Left;
                break;
        }

        return area;
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

        var glass = Rect.Inflate(_contentBounds, GlassPadding, GlassPadding);
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
            DockMenus.Populate(menu, item, _viewModel);
        }
    }

    private void OnGlassContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Glass.ContextMenu is { } menu)
        {
            DockMenus.Populate(menu, null, _viewModel);
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
                UpdatePlacement();
                break;
            case nameof(DockSettings.Roundness):
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
