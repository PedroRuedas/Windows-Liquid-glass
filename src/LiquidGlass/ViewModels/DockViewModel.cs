using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiquidGlass.Graphics;
using LiquidGlass.Models;
using LiquidGlass.Services;

namespace LiquidGlass.ViewModels;

/// <summary>
/// Monta a lista de ícones da dock (Iniciar, fixados, em execução, Lixeira) e reage aos cliques.
/// </summary>
public sealed partial class DockViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RemoveDelay = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(8);

    /// <summary>Duração de um quique completo (DockItemView) na velocidade 1×.</summary>
    private static readonly TimeSpan BounceCycle = TimeSpan.FromSeconds(0.62);

    private readonly WindowTracker _tracker;
    private readonly IconService _icons;
    private readonly BatteryService _battery;
    private readonly NetworkService _network;
    private readonly WindowAnimator _animator;
    private readonly DockItemViewModel _startItem;
    private readonly DockItemViewModel _workspacesItem;
    private readonly DockItemViewModel _batteryItem;
    private readonly DockItemViewModel _networkItem;
    private readonly DockItemViewModel _separatorItem;
    private readonly DockItemViewModel _recycleBinItem;
    private readonly DockItemViewModel _trayItem;
    private (int Percent, bool Charging, bool Saver) _batteryIconState;
    private (NetworkKind Kind, int Bars, bool HasWifi)? _networkIconState;

    public DockViewModel(
        DockSettings settings,
        WindowTracker tracker,
        IconService icons,
        BatteryService battery,
        NetworkService network,
        WindowAnimator animator,
        DockIcons dockIcons)
    {
        Settings = settings;
        _tracker = tracker;
        _icons = icons;
        _battery = battery;
        _network = network;
        _animator = animator;

        _startItem = new DockItemViewModel(this, DockItemKind.StartButton) { Name = "Iniciar", Icon = dockIcons.Start };
        _workspacesItem = new DockItemViewModel(this, DockItemKind.WorkspacesButton) { Name = "Áreas de trabalho", Icon = dockIcons.Workspaces };
        _batteryItem = new DockItemViewModel(this, DockItemKind.Battery);
        _networkItem = new DockItemViewModel(this, DockItemKind.Network);
        UpdateBatteryItem();
        UpdateNetworkItem();
        _separatorItem = new DockItemViewModel(this, DockItemKind.Separator);
        _recycleBinItem = new DockItemViewModel(this, DockItemKind.RecycleBin)
        {
            Name = "Lixeira",
            Icon = icons.GetIcon(AppLauncher.RecycleBin),
        };

        _trayItem = new DockItemViewModel(this, DockItemKind.TrayButton) { Name = "Apps em segundo plano", Icon = dockIcons.Tray };

        Settings.PropertyChanged += OnSettingsChanged;
        _battery.PropertyChanged += OnBatteryChanged;
        _network.PropertyChanged += OnNetworkChanged;
        Settings.PinnedApps.CollectionChanged += OnPinnedAppsChanged;
        _tracker.WindowsChanged += OnWindowsChanged;
        _tracker.ForegroundChanged += OnForegroundChanged;
        _tracker.WindowFlashed += OnWindowFlashed;

        Rebuild();
    }

    /// <summary>Pedido para abrir a janela de personalização.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Pedido para abrir ou fechar o painel da bandeja ao lado do item informado.</summary>
    public event EventHandler<DockItemViewModel>? TrayToggleRequested;

    /// <summary>Pedido para encerrar o app.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Pedido para procurar atualizações, mostrando o resultado na personalização.</summary>
    public event EventHandler? UpdatesRequested;

    /// <summary>Pedido para abrir ou fechar a visão das áreas de trabalho.</summary>
    public event EventHandler? WorkspacesRequested;

    /// <summary>Um ícone foi clicado; a visão das áreas de trabalho fecha para o app aparecer.</summary>
    public event EventHandler<DockItemViewModel>? ItemActivating;

    public DockSettings Settings { get; }

    /// <summary>Atualizações pelo menu da dock; sem ele, o menu não as oferece.</summary>
    public UpdateService? Updates { get; init; }

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    /// <summary>Ampliação aplicada ao painel (1 quando desativada).</summary>
    public double EffectiveMagnification => Settings.MagnificationEnabled ? Math.Max(Settings.Magnification, 1) : 1;

    public void Activate(DockItemViewModel item)
    {
        ItemActivating?.Invoke(this, item);
        switch (item.Kind)
        {
            case DockItemKind.StartButton:
                AppLauncher.OpenStartMenu();
                break;
            case DockItemKind.WorkspacesButton:
                WorkspacesRequested?.Invoke(this, EventArgs.Empty);
                break;
            case DockItemKind.Battery:
                AppLauncher.Launch(AppLauncher.PowerSettings);
                break;
            case DockItemKind.Network:
                AppLauncher.Launch(_network.HasWifi ? AppLauncher.WifiNetworks : AppLauncher.NetworkSettings);
                break;
            case DockItemKind.RecycleBin:
                AppLauncher.Launch(AppLauncher.RecycleBin);
                break;
            case DockItemKind.TrayButton:
                TrayToggleRequested?.Invoke(this, item);
                break;
            case DockItemKind.Application when item.IsRunning:
                FocusOrCycle(item);
                break;
            case DockItemKind.Application:
                Launch(item);
                break;
        }
    }

    public void OpenNew(DockItemViewModel item)
    {
        if (item.Kind == DockItemKind.Application)
        {
            Launch(item);
        }
        else
        {
            Activate(item);
        }
    }

    public void TogglePin(DockItemViewModel item)
    {
        if (item.Pin is { } pin)
        {
            Settings.PinnedApps.Remove(pin);
        }
        else if (item.Windows.Count > 0)
        {
            Settings.PinnedApps.Add(PinnedAppFactory.FromWindow(item.Windows[0], item.Name));
        }
    }

    public void PinPath(string path)
    {
        if (PinnedAppFactory.FromPath(path) is { } pin && !Settings.PinnedApps.Any(existing => existing.Target == pin.Target))
        {
            Settings.PinnedApps.Add(pin);
        }
    }

    public void CloseAll(DockItemViewModel item)
    {
        foreach (var window in item.Windows)
        {
            _animator.Close(window.Handle);
        }
    }

    public void ActivateWindow(DockItemViewModel item, AppWindow window)
    {
        item.NeedsAttention = false;
        _animator.Activate(window.Handle);
    }

    /// <summary>Item da dock que mostra a janela (o app dono dela), se houver.</summary>
    public DockItemViewModel? FindItem(nint hwnd) => Items.FirstOrDefault(item => item.Windows.Any(window => window.Handle == hwnd));

    public void Dispose()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        _battery.PropertyChanged -= OnBatteryChanged;
        _network.PropertyChanged -= OnNetworkChanged;
        Settings.PinnedApps.CollectionChanged -= OnPinnedAppsChanged;
        _tracker.WindowsChanged -= OnWindowsChanged;
        _tracker.ForegroundChanged -= OnForegroundChanged;
        _tracker.WindowFlashed -= OnWindowFlashed;
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CheckForUpdates() => UpdatesRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>A personalização abre junto, mostrando o download e qualquer erro.</summary>
    [RelayCommand]
    private Task InstallUpdate()
    {
        UpdatesRequested?.Invoke(this, EventArgs.Empty);
        return Updates?.InstallAsync() ?? Task.CompletedTask;
    }

    /// <summary>
    /// Com o app em foco, minimiza (uma janela) ou alterna entre as janelas; senão, traz a mais recente.
    /// </summary>
    private void FocusOrCycle(DockItemViewModel item)
    {
        item.NeedsAttention = false;
        var windows = item.Windows;
        var foreground = _tracker.ForegroundWindow;
        var isFocused = windows.Any(window => window.Handle == foreground);

        if (!isFocused)
        {
            _animator.Activate(windows[0].Handle);
        }
        else if (windows.Count == 1)
        {
            _animator.Minimize(foreground);
        }
        else
        {
            // As janelas vêm em ordem Z: a última é a usada há mais tempo.
            _animator.Activate(windows[^1].Handle);
        }
    }

    private async void Launch(DockItemViewModel item)
    {
        var target = item.Pin?.Target ?? (item.IsRunning ? item.Windows[0].ExecutablePath : null);
        if (target is null || !AppLauncher.Launch(target) || !Settings.BounceOnLaunch || item.IsRunning)
        {
            return;
        }

        item.LaunchStartedAt = DateTime.Now;
        item.IsLaunching = true;
        await Task.Delay(LaunchTimeout);
        item.IsLaunching = false;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DockSettings.ShowStartButton):
            case nameof(DockSettings.ShowRecycleBin):
            case nameof(DockSettings.ShowTray):
            case nameof(DockSettings.ShowBattery):
            case nameof(DockSettings.ShowWorkspacesButton):
            case nameof(DockSettings.ShowNetwork):
                Rebuild();
                break;
            case nameof(DockSettings.MagnificationEnabled):
            case nameof(DockSettings.Magnification):
                OnPropertyChanged(nameof(EffectiveMagnification));
                break;
        }
    }

    private void OnPinnedAppsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnBatteryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BatteryService.IsPresent))
        {
            Rebuild();
        }

        UpdateBatteryItem();
    }

    private void OnNetworkChanged(object? sender, PropertyChangedEventArgs e) => UpdateNetworkItem();

    private void UpdateNetworkItem()
    {
        _networkItem.Name = _network.Describe();
        var state = (_network.Kind, NetworkIcon.Bars(_network.Signal), _network.HasWifi);
        if (state != _networkIconState)
        {
            _networkIconState = state;
            _networkItem.Icon = NetworkIcon.Create(state.Kind, state.Item2, state.HasWifi);
        }
    }

    /// <summary>O rótulo acompanha cada mudança; o ícone só é redesenhado quando o que ele mostra muda.</summary>
    private void UpdateBatteryItem()
    {
        _batteryItem.Name = _battery.Describe();
        var state = (_battery.Percent, _battery.IsCharging, _battery.IsSaverOn);
        if (_batteryItem.Icon is null || state != _batteryIconState)
        {
            _batteryIconState = state;
            _batteryItem.Icon = BatteryIcon.Create(state.Percent, state.IsCharging, state.IsSaverOn);
        }
    }

    private void OnWindowsChanged(object? sender, EventArgs e) => Rebuild();

    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        foreach (var item in Items)
        {
            item.IsActive = item.Windows.Any(window => window.Handle == hwnd);
            if (item.IsActive)
            {
                item.NeedsAttention = false;
            }
        }
    }

    private void OnWindowFlashed(object? sender, nint hwnd)
    {
        if (!Settings.BounceOnAttention)
        {
            return;
        }

        var item = Items.FirstOrDefault(candidate => candidate.Windows.Any(window => window.Handle == hwnd));
        if (item is { IsActive: false })
        {
            item.NeedsAttention = true;
        }
    }

    /// <summary>Recalcula a lista desejada de itens e aplica só as diferenças, para preservar as animações.</summary>
    private void Rebuild()
    {
        var reusable = Items.Where(item => item.IsApplication).ToList();
        var unclaimed = _tracker.Windows.ToList();
        var desired = new List<DockItemViewModel>();

        if (Settings.ShowStartButton)
        {
            desired.Add(_startItem);
        }

        if (Settings.ShowWorkspacesButton)
        {
            desired.Add(_workspacesItem);
        }

        foreach (var pin in Settings.PinnedApps)
        {
            var windows = ClaimWindows(pin, unclaimed);
            var item = TakeReusable(reusable, windows, candidate => candidate.Pin == pin)
                ?? CreateApplicationItem(pin.Name, pin.Target, windows);

            item.Pin = pin;
            item.Name = pin.Name;
            item.GroupKey = null;
            UpdateWindows(item, windows);
            desired.Add(item);
        }

        var running = new List<DockItemViewModel>();
        foreach (var group in unclaimed.GroupBy(window => window.GroupKey))
        {
            var windows = group.ToList();
            var item = TakeReusable(reusable, windows, candidate => candidate.Pin is null && candidate.GroupKey == group.Key)
                ?? CreateApplicationItem(PinnedAppFactory.GetDisplayName(windows[0]), PinnedAppFactory.GetIconSource(windows[0]), windows);

            item.Pin = null;
            item.GroupKey = group.Key;
            UpdateWindows(item, windows);
            running.Add(item);
        }

        // Apps em execução mantêm a ordem em que apareceram; os novos entram no final.
        desired.AddRange(running.OrderBy(item => Items.IndexOf(item) is var index and >= 0 ? index : int.MaxValue));

        var showBattery = Settings.ShowBattery && _battery.IsPresent;
        if (Settings.ShowTray || Settings.ShowNetwork || showBattery || Settings.ShowRecycleBin)
        {
            desired.Add(_separatorItem);
        }

        if (Settings.ShowTray)
        {
            desired.Add(_trayItem);
        }

        if (Settings.ShowNetwork)
        {
            desired.Add(_networkItem);
        }

        if (showBattery)
        {
            desired.Add(_batteryItem);
        }

        if (Settings.ShowRecycleBin)
        {
            desired.Add(_recycleBinItem);
        }

        Reconcile(desired);
        OnForegroundChanged(this, _tracker.ForegroundWindow);
    }

    private static List<AppWindow> ClaimWindows(PinnedApp pin, List<AppWindow> unclaimed)
    {
        var claimed = unclaimed.Where(window => Matches(pin, window)).ToList();
        unclaimed.RemoveAll(claimed.Contains);
        return claimed;
    }

    private static bool Matches(PinnedApp pin, AppWindow window)
    {
        if (pin.AppUserModelId is not null && window.AppUserModelId is not null)
        {
            return string.Equals(pin.AppUserModelId, window.AppUserModelId, StringComparison.OrdinalIgnoreCase);
        }

        var executable = PinnedAppFactory.ResolveExecutable(pin);
        return executable is not null
            && string.Equals(executable, window.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reaproveita um item que já mostra alguma dessas janelas (ex.: app recém-fixado).</summary>
    private static DockItemViewModel? TakeReusable(
        List<DockItemViewModel> reusable,
        List<AppWindow> windows,
        Func<DockItemViewModel, bool> fallback)
    {
        var handles = windows.Select(window => window.Handle).ToHashSet();
        var item = reusable.FirstOrDefault(candidate => candidate.Windows.Any(window => handles.Contains(window.Handle)))
            ?? reusable.FirstOrDefault(fallback);

        if (item is not null)
        {
            reusable.Remove(item);
            item.IsRemoving = false;
        }

        return item;
    }

    private void UpdateWindows(DockItemViewModel item, List<AppWindow> windows)
    {
        item.Windows = windows;
        if (windows.Count > 0 && item.IsLaunching)
        {
            FinishLaunch(item);
        }
    }

    /// <summary>Apps que abrem muito rápido ainda completam um quique, como no macOS.</summary>
    private async void FinishLaunch(DockItemViewModel item)
    {
        var cycle = BounceCycle / Math.Max(Settings.AnimationSpeed, 0.1);
        var remaining = item.LaunchStartedAt + cycle - DateTime.Now;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }

        item.IsLaunching = false;
    }

    private DockItemViewModel CreateApplicationItem(string name, string? iconSource, List<AppWindow> windows)
    {
        var icon = iconSource is null ? null : _icons.GetIcon(iconSource);
        if (icon is null && windows.Count > 0)
        {
            icon = IconService.GetWindowIcon(windows[0].Handle);
        }

        return new DockItemViewModel(this, DockItemKind.Application) { Name = name, Icon = icon };
    }

    private void Reconcile(List<DockItemViewModel> desired)
    {
        foreach (var item in Items.Where(item => !desired.Contains(item) && !item.IsRemoving).ToList())
        {
            BeginRemove(item);
        }

        var index = 0;
        foreach (var item in desired)
        {
            while (index < Items.Count && Items[index].IsRemoving && Items[index] != item)
            {
                index++;
            }

            if (index < Items.Count && Items[index] == item)
            {
                index++;
                continue;
            }

            var current = Items.IndexOf(item);
            if (current >= 0)
            {
                Items.Move(current, current < index ? index - 1 : index);
            }
            else
            {
                Items.Insert(index, item);
            }

            index = Items.IndexOf(item) + 1;
        }
    }

    private async void BeginRemove(DockItemViewModel item)
    {
        item.IsRemoving = true;
        await Task.Delay(RemoveDelay / Math.Max(Settings.AnimationSpeed, 0.1));
        if (item.IsRemoving)
        {
            Items.Remove(item);
        }
    }
}

/// <summary>Ícones desenhados (recursos XAML) dos botões fixos da dock.</summary>
public sealed record DockIcons(ImageSource Start, ImageSource Tray, ImageSource Workspaces);
