using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiquidGlass.Graphics;
using LiquidGlass.Models;
using LiquidGlass.Resources;
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

        _startItem = new DockItemViewModel(this, DockItemKind.StartButton) { Name = Strings.StartTitle, Icon = dockIcons.Start };
        _workspacesItem = new DockItemViewModel(this, DockItemKind.WorkspacesButton) { Name = Strings.Desktops, Icon = dockIcons.Workspaces };
        _batteryItem = new DockItemViewModel(this, DockItemKind.Battery);
        _networkItem = new DockItemViewModel(this, DockItemKind.Network);
        UpdateBatteryItem();
        UpdateNetworkItem();
        _separatorItem = new DockItemViewModel(this, DockItemKind.Separator);
        _recycleBinItem = new DockItemViewModel(this, DockItemKind.RecycleBin)
        {
            Name = Strings.RecycleBin,
            Icon = icons.GetIcon(AppLauncher.RecycleBin),
        };

        _trayItem = new DockItemViewModel(this, DockItemKind.TrayButton) { Name = Strings.BackgroundApps, Icon = dockIcons.Tray };

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

    /// <summary>O botão Iniciar pede o menu de vidro (com ele ligado nas preferências).</summary>
    public event EventHandler? StartMenuRequested;

    /// <summary>Uma pasta fixada foi clicada: a dock abre a pilha dela.</summary>
    public event EventHandler<DockItemViewModel>? StackRequested;

    /// <summary>Um ícone foi clicado; a visão das áreas de trabalho fecha para o app aparecer.</summary>
    public event EventHandler<DockItemViewModel>? ItemActivating;

    public DockSettings Settings { get; }

    /// <summary>Atualizações pelo menu da dock; sem ele, o menu não as oferece.</summary>
    public UpdateService? Updates { get; init; }

    /// <summary>O que os apps estão tocando, para o cartão de mídia das miniaturas.</summary>
    public MediaService? Media { get; init; }

    /// <summary>Selos e barras de progresso que os apps mandam para a barra de tarefas.</summary>
    public TaskbarListService? TaskbarList
    {
        get => _taskbarList;
        init
        {
            _taskbarList = value;
            if (value is not null)
            {
                value.Changed += (_, _) => UpdateTaskbarState();
            }
        }
    }

    private readonly TaskbarListService? _taskbarList;

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    /// <summary>Ampliação aplicada ao painel (1 quando desativada).</summary>
    public double EffectiveMagnification => Settings.MagnificationEnabled ? Math.Max(Settings.Magnification, 1) : 1;

    /// <summary>
    /// Win+1…9: o app na posição <paramref name="index"/>, contando só os apps (fixados e abertos)
    /// na ordem da dock. Faz o mesmo que o clique; com <paramref name="newWindow"/>, o botão do meio.
    /// </summary>
    public bool ActivateByNumber(int index, bool newWindow)
    {
        var apps = Items.Where(item => item.IsApplication && !item.IsRemoving).ToList();
        if (index < 0 || index >= apps.Count)
        {
            return false;
        }

        if (newWindow)
        {
            OpenNew(apps[index]);
        }
        else
        {
            Activate(apps[index]);
        }

        return true;
    }

    public void Activate(DockItemViewModel item)
    {
        ItemActivating?.Invoke(this, item);
        switch (item.Kind)
        {
            case DockItemKind.StartButton when Settings.UseGlassStartMenu && StartMenuRequested is not null:
                StartMenuRequested.Invoke(this, EventArgs.Empty);
                break;
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
            case DockItemKind.Application when StackRequested is not null && StackService.IsFolder(item.Pin?.Target):
                StackRequested.Invoke(this, item);
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

    /// <summary>
    /// Apps com que o item pode trocar de lugar, na ordem da dock: os fixados entre si (a ordem
    /// fica salva) e os abertos não fixados entre si. Vazio para os botões fixos da dock.
    /// </summary>
    public List<DockItemViewModel> ReorderGroup(DockItemViewModel item)
    {
        return item.IsApplication && !item.IsRemoving
            ? Items.Where(candidate => candidate.IsApplication && !candidate.IsRemoving && candidate.IsPinned == item.IsPinned).ToList()
            : [];
    }

    /// <summary>Leva o item para a posição <paramref name="index"/> dentro do <see cref="ReorderGroup"/> dele.</summary>
    public void MoveWithinGroup(DockItemViewModel item, int index)
    {
        var group = ReorderGroup(item);
        var from = group.IndexOf(item);
        index = Math.Clamp(index, 0, group.Count - 1);
        if (from < 0 || from == index)
        {
            return;
        }

        var target = group[index];
        if (item.Pin is { } pin && target.Pin is { } targetPin)
        {
            // A dock se refaz a partir da lista de fixados (Rebuild), e a nova ordem é salva.
            Settings.PinnedApps.Move(Settings.PinnedApps.IndexOf(pin), Settings.PinnedApps.IndexOf(targetPin));
        }
        else
        {
            // Os abertos mantêm a ordem em que estão na dock (Rebuild a preserva), e ela é salva.
            Items.Move(Items.IndexOf(item), Items.IndexOf(target));
            var keys = ReorderGroup(item).Select(other => RunningOrder.KeyOf(other.GroupKey)).OfType<string>().ToList();
            Settings.RunningOrder = RunningOrder.Remember(Settings.RunningOrder, keys);
        }
    }

    /// <summary>
    /// Fixa um app aberto na posição <paramref name="index"/> entre os fixados, como ao arrastá-lo
    /// para o meio deles. O mesmo ícone passa a ser o fixado (Rebuild o reaproveita pelas janelas).
    /// </summary>
    public void PinAt(DockItemViewModel item, int index)
    {
        if (item.Pin is not null || item.Windows.Count == 0)
        {
            return;
        }

        var pin = PinnedAppFactory.FromWindow(item.Windows[0], item.Name);
        if (Settings.PinnedApps.Any(existing => existing.Target.Equals(pin.Target, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Settings.PinnedApps.Insert(Math.Clamp(index, 0, Settings.PinnedApps.Count), pin);
    }

    /// <summary>Os fixados, na ordem da dock: o destino de um app aberto arrastado para o meio deles.</summary>
    public List<DockItemViewModel> PinnedItems() =>
        Items.Where(candidate => candidate.IsApplication && !candidate.IsRemoving && candidate.IsPinned).ToList();

    /// <summary>"Trocar ícone…": uma imagem (PNG, ICO, JPG) ou o ícone de outro programa.</summary>
    public void ChangeIcon(DockItemViewModel item)
    {
        if (item.IconKey is not { } key)
        {
            return;
        }

        var extensions = string.Join(";", CustomIcons.ImageExtensions.Concat(CustomIcons.ProgramExtensions).Select(extension => "*" + extension));
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.ChooseIconTitle,
            Filter = Strings.ChooseIconFilter(extensions),
        };

        if (dialog.ShowDialog() != true || CustomIcons.Import(dialog.FileName) is not { } stored)
        {
            return;
        }

        var icons = new Dictionary<string, string>(Settings.CustomIcons, StringComparer.OrdinalIgnoreCase);
        CustomIcons.Delete(icons.GetValueOrDefault(key));
        icons[key] = stored;
        Settings.CustomIcons = icons;
        _customIconCache.Remove(stored);
        ApplyCustomIcons();
    }

    public void ResetIcon(DockItemViewModel item)
    {
        if (item.IconKey is not { } key || !Settings.CustomIcons.ContainsKey(key))
        {
            return;
        }

        var icons = new Dictionary<string, string>(Settings.CustomIcons, StringComparer.OrdinalIgnoreCase);
        CustomIcons.Delete(icons[key]);
        icons.Remove(key);
        Settings.CustomIcons = icons;
        ApplyCustomIcons();
    }

    /// <summary>
    /// Selo e progresso de cada app, a partir das janelas dele: o selo da primeira janela que tem um
    /// e o progresso da primeira que está mostrando algum (como a barra do Windows faz com o grupo).
    /// </summary>
    private void UpdateTaskbarState()
    {
        foreach (var item in Items.Where(item => item.IsApplication))
        {
            item.Badge = Settings.ShowBadges && _taskbarList is { } list
                ? item.Windows.Select(window => list.OverlayFor(window.Handle)).FirstOrDefault(icon => icon is not null)
                : null;

            var progress = Settings.ShowBadges && _taskbarList is { } source
                ? item.Windows.Select(window => source.ProgressFor(window.Handle)).FirstOrDefault(state => state.State != TaskbarProgressState.None)
                : default;
            item.ProgressState = progress.State;
            item.Progress = progress.Value;
        }
    }

    /// <summary>Põe o ícone escolhido pelo usuário (ou de volta o do app) em cada app da dock.</summary>
    private void ApplyCustomIcons()
    {
        foreach (var item in Items.Where(item => item.IsApplication))
        {
            var path = item.IconKey is { } key && Settings.CustomIcons.TryGetValue(key, out var stored) ? stored : null;
            var custom = path is null ? null : LoadCustomIcon(path);
            item.HasCustomIcon = custom is not null;
            item.Icon = custom ?? item.DefaultIcon;
        }
    }

    private ImageSource? LoadCustomIcon(string path)
    {
        if (!_customIconCache.TryGetValue(path, out var icon))
        {
            icon = CustomIcons.Load(path);
            _customIconCache[path] = icon;
        }

        return icon;
    }

    private readonly Dictionary<string, ImageSource?> _customIconCache = new(StringComparer.OrdinalIgnoreCase);

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

    public void CloseWindow(AppWindow window) => _animator.Close(window.Handle);

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
        // Apps da Store não fixados abrem pelo AUMID: o executável deles é só o host (ApplicationFrameHost).
        var target = item.Pin?.Target ?? (item.IsRunning ? PinnedAppFactory.FromWindow(item.Windows[0], item.Name).Target : null);
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
            case nameof(DockSettings.ShowBadges):
                UpdateTaskbarState();
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

        // Os que já estão na dock mantêm o lugar; os que abriram entram na posição que tinham na
        // ordem salva (a arrastada pelo usuário, inclusive antes de reiniciar a dock) ou no final.
        var existing = running.Where(Items.Contains).OrderBy(Items.IndexOf);
        var added = running.Where(item => !Items.Contains(item));
        desired.AddRange(RunningOrder.Arrange(existing, added, item => RunningOrder.KeyOf(item.GroupKey), Settings.RunningOrder));

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
        ApplyCustomIcons();
        UpdateTaskbarState();
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

        return new DockItemViewModel(this, DockItemKind.Application) { Name = name, Icon = icon, DefaultIcon = icon };
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
