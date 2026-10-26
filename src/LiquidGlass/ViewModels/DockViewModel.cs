using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    private readonly WindowTracker _tracker;
    private readonly IconService _icons;
    private readonly DockItemViewModel _startItem;
    private readonly DockItemViewModel _separatorItem;
    private readonly DockItemViewModel _recycleBinItem;

    public DockViewModel(DockSettings settings, WindowTracker tracker, IconService icons, ImageSource startIcon)
    {
        Settings = settings;
        _tracker = tracker;
        _icons = icons;

        _startItem = new DockItemViewModel(this, DockItemKind.StartButton) { Name = "Iniciar", Icon = startIcon };
        _separatorItem = new DockItemViewModel(this, DockItemKind.Separator);
        _recycleBinItem = new DockItemViewModel(this, DockItemKind.RecycleBin)
        {
            Name = "Lixeira",
            Icon = icons.GetIcon(AppLauncher.RecycleBin),
        };

        Settings.PropertyChanged += OnSettingsChanged;
        Settings.PinnedApps.CollectionChanged += OnPinnedAppsChanged;
        _tracker.WindowsChanged += OnWindowsChanged;
        _tracker.ForegroundChanged += OnForegroundChanged;
        _tracker.WindowFlashed += OnWindowFlashed;

        Rebuild();
    }

    /// <summary>Pedido para abrir a janela de personalização.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Pedido para encerrar o app.</summary>
    public event EventHandler? QuitRequested;

    public DockSettings Settings { get; }

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    /// <summary>Ampliação aplicada ao painel (1 quando desativada).</summary>
    public double EffectiveMagnification => Settings.MagnificationEnabled ? Math.Max(Settings.Magnification, 1) : 1;

    public void Activate(DockItemViewModel item)
    {
        switch (item.Kind)
        {
            case DockItemKind.StartButton:
                AppLauncher.OpenStartMenu();
                break;
            case DockItemKind.RecycleBin:
                AppLauncher.Launch(AppLauncher.RecycleBin);
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

    public static void CloseAll(DockItemViewModel item)
    {
        foreach (var window in item.Windows)
        {
            AppLauncher.Close(window.Handle);
        }
    }

    public static void ActivateWindow(DockItemViewModel item, AppWindow window)
    {
        item.NeedsAttention = false;
        AppLauncher.Activate(window.Handle);
    }

    public void Dispose()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        Settings.PinnedApps.CollectionChanged -= OnPinnedAppsChanged;
        _tracker.WindowsChanged -= OnWindowsChanged;
        _tracker.ForegroundChanged -= OnForegroundChanged;
        _tracker.WindowFlashed -= OnWindowFlashed;
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);

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
            AppLauncher.Activate(windows[0].Handle);
        }
        else if (windows.Count == 1)
        {
            AppLauncher.Minimize(foreground);
        }
        else
        {
            // As janelas vêm em ordem Z: a última é a usada há mais tempo.
            AppLauncher.Activate(windows[^1].Handle);
        }
    }

    private async void Launch(DockItemViewModel item)
    {
        var target = item.Pin?.Target ?? (item.IsRunning ? item.Windows[0].ExecutablePath : null);
        if (target is null || !AppLauncher.Launch(target) || !Settings.BounceOnLaunch || item.IsRunning)
        {
            return;
        }

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
                Rebuild();
                break;
            case nameof(DockSettings.MagnificationEnabled):
            case nameof(DockSettings.Magnification):
                OnPropertyChanged(nameof(EffectiveMagnification));
                break;
        }
    }

    private void OnPinnedAppsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

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

        if (Settings.ShowRecycleBin)
        {
            desired.Add(_separatorItem);
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

        return pin.ExecutablePath is not null
            && string.Equals(pin.ExecutablePath, window.ExecutablePath, StringComparison.OrdinalIgnoreCase);
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

    private static void UpdateWindows(DockItemViewModel item, List<AppWindow> windows)
    {
        item.Windows = windows;
        if (windows.Count > 0)
        {
            item.IsLaunching = false;
        }
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
