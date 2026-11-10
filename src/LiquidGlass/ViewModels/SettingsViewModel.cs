using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiquidGlass.Models;
using LiquidGlass.Resources;
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

    public SettingsViewModel(DockSettings settings, IconService icons, UpdateService updates)
    {
        Settings = settings;
        _icons = icons;
        Updates = updates;
        Settings.PinnedApps.CollectionChanged += OnPinnedAppsChanged;
        Settings.PropertyChanged += OnSettingsChanged;
        Updates.PropertyChanged += OnUpdatesChanged;
        RefreshPins();
    }

    /// <summary>Pedido para fechar a dock e abri-la de novo (o idioma só vale ao abrir).</summary>
    public event EventHandler? RestartRequested;

    public DockSettings Settings { get; }

    public UpdateService Updates { get; }

    /// <summary>Com uma versão nova encontrada, o botão instala; senão, procura.</summary>
    public string UpdateActionText => Updates.Available is not null ? Strings.UpdateNow : Strings.CheckForUpdates;

    public string UpdateStatus => Updates.State == UpdateState.Idle
        ? Strings.CurrentVersion(Version)
        : Updates.Message;

    public bool IsDownloading => Updates.State == UpdateState.Downloading;

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

    public string VersionText => Strings.SettingsVersion(Version);

    /// <summary>Português e English aparecem sempre no próprio idioma, para quem precisa trocar achar.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(AppLanguage.Automatic, Strings.LanguageAutomatic),
        new(AppLanguage.Portuguese, Strings.LanguagePortuguese),
        new(AppLanguage.English, Strings.LanguageEnglish),
    ];

    /// <summary>O idioma escolhido não é o que está na tela: só vale depois de reiniciar a dock.</summary>
    public bool LanguageNeedsRestart => !Strings.IsInUse(Settings.Language);

    public string LanguageDescription => LanguageNeedsRestart ? Strings.LanguageRestartHint : Strings.LanguageDescription;

    /// <summary>Depois de copiar, a descrição confirma e diz o que fazer com o texto.</summary>
    [ObservableProperty]
    private string _diagnosticsDescription = Strings.DiagnosticsDescription;

    public void Dispose()
    {
        Settings.PinnedApps.CollectionChanged -= OnPinnedAppsChanged;
        Settings.PropertyChanged -= OnSettingsChanged;
        Updates.PropertyChanged -= OnUpdatesChanged;
    }

    [RelayCommand]
    private void Restart() => RestartRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CopyDiagnostics()
    {
        try
        {
            System.Windows.Clipboard.SetText(Diagnostics.Build(Settings));
            DiagnosticsDescription = Strings.DiagnosticsCopied;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Outro app segurando a área de transferência: o clique seguinte tenta de novo.
        }
    }

    /// <summary>Abre o formulário de bug do GitHub; as informações de diagnóstico já vão copiadas.</summary>
    [RelayCommand]
    private void ReportProblem()
    {
        CopyDiagnostics();
        _ = AppLauncher.Launch(IssueUrl);
    }

    private const string IssueUrl = "https://github.com/PedroRuedas/Windows-Liquid-glass/issues/new?template=bug_report.yml";

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DockSettings.Language))
        {
            OnPropertyChanged(nameof(LanguageNeedsRestart));
            OnPropertyChanged(nameof(LanguageDescription));
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunUpdateAction))]
    private Task UpdateAction() => Updates.Available is not null ? Updates.InstallAsync() : Updates.CheckAsync(userInitiated: true);

    private bool CanRunUpdateAction() => !Updates.IsBusy;

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(UpdateActionText));
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(IsDownloading));
        UpdateActionCommand.NotifyCanExecuteChanged();
    }

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
            Title = Strings.ChooseAppTitle,
            Filter = Strings.ChooseAppFilter,
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

/// <summary>Uma opção da lista de idiomas.</summary>
public sealed record LanguageOption(AppLanguage Value, string Name);
