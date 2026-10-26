using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiquidGlass.Models;
using LiquidGlass.Services;
using Microsoft.Win32;

namespace LiquidGlass.ViewModels;

/// <summary>Estado da janela de personalização. As alterações vão direto para <see cref="DockSettings"/>.</summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IconService _icons;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemovePinCommand), nameof(MovePinUpCommand), nameof(MovePinDownCommand))]
    private PinnedAppItem? _selectedPin;

    public SettingsViewModel(DockSettings settings, IconService icons)
    {
        Settings = settings;
        _icons = icons;
        Settings.PinnedApps.CollectionChanged += OnPinnedAppsChanged;
        RefreshPins();
    }

    public DockSettings Settings { get; }

    public ObservableCollection<PinnedAppItem> Pins { get; } = [];

    public IReadOnlyList<GlassPreset> Presets { get; } = GlassPreset.All;

    public IReadOnlyList<Color> TintSwatches { get; } =
    [
        Colors.White,
        Color.FromRgb(0x10, 0x10, 0x14),
        Color.FromRgb(0x4C, 0xA8, 0xFF),
        Color.FromRgb(0xB4, 0x7C, 0xFF),
        Color.FromRgb(0xFF, 0x6B, 0xB5),
        Color.FromRgb(0xFF, 0x9F, 0x43),
        Color.FromRgb(0x4C, 0xD9, 0x8A),
    ];

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public void Dispose() => Settings.PinnedApps.CollectionChanged -= OnPinnedAppsChanged;

    [RelayCommand]
    private void ApplyPreset(GlassPreset preset) => preset.ApplyTo(Settings);

    [RelayCommand]
    private void SetTint(Color color) => Settings.TintColor = color;

    [RelayCommand]
    private void ResetDefaults() => Settings.CopyPreferencesFrom(new DockSettings());

    [RelayCommand]
    private void AddApp()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolha um aplicativo ou atalho",
            Filter = "Aplicativos e atalhos (*.exe;*.lnk)|*.exe;*.lnk|Todos os arquivos (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            AddPin(PinnedAppFactory.FromPath(path));
        }
    }

    [RelayCommand]
    private void ImportTaskbarPins()
    {
        foreach (var pin in PinnedAppFactory.ImportTaskbarPins())
        {
            AddPin(pin);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemovePin()
    {
        if (SelectedPin is { } selected)
        {
            Settings.PinnedApps.Remove(selected.Pin);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MovePinUp() => MoveSelected(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MovePinDown() => MoveSelected(1);

    private bool HasSelection() => SelectedPin is not null;

    private bool CanMoveUp() => SelectedPin is not null && Pins.IndexOf(SelectedPin) > 0;

    private bool CanMoveDown() => SelectedPin is not null && Pins.IndexOf(SelectedPin) < Pins.Count - 1;

    private void AddPin(PinnedApp? pin)
    {
        if (pin is not null && !Settings.PinnedApps.Any(existing => existing.Target.Equals(pin.Target, StringComparison.OrdinalIgnoreCase)))
        {
            Settings.PinnedApps.Add(pin);
        }
    }

    private void MoveSelected(int offset)
    {
        if (SelectedPin is not { } selected)
        {
            return;
        }

        var index = Settings.PinnedApps.IndexOf(selected.Pin);
        Settings.PinnedApps.Move(index, index + offset);
        SelectedPin = Pins.FirstOrDefault(item => item.Pin == selected.Pin);
    }

    private void OnPinnedAppsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshPins();

    private void RefreshPins()
    {
        var selected = SelectedPin?.Pin;
        Pins.Clear();
        foreach (var pin in Settings.PinnedApps)
        {
            Pins.Add(new PinnedAppItem(pin, _icons.GetIcon(pin.Target)));
        }

        SelectedPin = Pins.FirstOrDefault(item => item.Pin == selected);
        MovePinUpCommand.NotifyCanExecuteChanged();
        MovePinDownCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>App fixado com o ícone já carregado, para a lista da janela de personalização.</summary>
public sealed record PinnedAppItem(PinnedApp Pin, ImageSource? Icon);
