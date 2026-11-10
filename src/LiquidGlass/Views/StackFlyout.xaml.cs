using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LiquidGlass.Graphics;
using LiquidGlass.Models;
using LiquidGlass.Services;

namespace LiquidGlass.Views;

/// <summary>Um item da pilha com o ícone, que chega depois, carregado numa thread de fundo.</summary>
public sealed partial class StackTile(StackEntry entry) : ObservableObject
{
    [ObservableProperty]
    private ImageSource? _icon;

    public StackEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public string Path => Entry.Path;
}

/// <summary>
/// Pilha de uma pasta fixada: uma grade de vidro com os arquivos mais recentes, aberta junto ao
/// ícone. Fecha quando o mouse sai, como o painel da bandeja.
/// </summary>
public partial class StackFlyout : Popup, IDisposable
{
    private const int IconPixels = 96;

    private readonly DispatcherTimer _hoverWatcher = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly ObservableCollection<StackTile> _tiles = [];
    private readonly StaWorker _worker = new("Pilha");
    private DockEdge _edge;
    private bool _isClosing;
    private long? _cursorLeftAt;
    private string? _folder;
    private int _version;

    public StackFlyout()
    {
        InitializeComponent();
        Entries.ItemsSource = _tiles;
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

    /// <summary>Abre a pilha da pasta junto ao ícone ou a fecha se ela já estiver aberta.</summary>
    public async void Toggle(FrameworkElement anchor, DockEdge edge, string folder, string title)
    {
        if (IsOpen && string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase))
        {
            CloseAnimated();
            return;
        }

        var version = ++_version;
        _edge = edge;
        _folder = folder;
        PlacementTarget = anchor;
        Title.Text = title;

        var entries = await Task.Run(() => StackService.Load(folder));
        if (version != _version)
        {
            return;
        }

        _tiles.Clear();
        foreach (var entry in entries)
        {
            _tiles.Add(new StackTile(entry));
        }

        EmptyText.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScreenBackdrop.Shared.CaptureBeside(anchor, edge);
        IsOpen = true;
        await LoadIconsAsync(version);
    }

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

    public void Dispose()
    {
        _worker.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Miniaturas (de imagens, inclusive) pelo shell, uma por vez, sem travar a dock.</summary>
    private async Task LoadIconsAsync(int version)
    {
        foreach (var tile in _tiles.ToList())
        {
            var icon = await _worker.Run<ImageSource?>(() => IconService.LoadIcon(tile.Path, IconPixels));
            if (version != _version)
            {
                return;
            }

            tile.Icon = icon;
        }
    }

    private void OnEntryClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StackTile tile })
        {
            AppLauncher.Launch(tile.Path);
            CloseAnimated();
            e.Handled = true;
        }
    }

    /// <summary>Botão direito: mostra o item selecionado no Explorador.</summary>
    private void OnEntryRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StackTile tile })
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{tile.Path}\"") { UseShellExecute = true })?.Dispose();
            CloseAnimated();
            e.Handled = true;
        }
    }

    private void OnOpenFolderClick(object sender, MouseButtonEventArgs e)
    {
        if (_folder is not null)
        {
            AppLauncher.Launch(_folder);
        }

        CloseAnimated();
        e.Handled = true;
    }

    /// <summary>Demonstração (Debug): fica aberta sem o mouse, para ser fotografada.</summary>
    internal bool IsDemo { get; set; }

    private void CloseWhenCursorLeaves()
    {
        if (IsDemo || DockPopupPlacement.IsCursorOver(PopupContent) || DockPopupPlacement.IsCursorOver(PlacementTarget as FrameworkElement))
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
}
