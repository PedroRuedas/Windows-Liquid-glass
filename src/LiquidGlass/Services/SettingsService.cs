using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using LiquidGlass.Models;

namespace LiquidGlass.Services;

/// <summary>
/// Carrega as preferências do disco e as salva automaticamente (com debounce) a cada alteração.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new ColorJsonConverter() },
    };

    private readonly DispatcherTimer _saveTimer;

    public SettingsService()
    {
        AppDataPaths.EnsureRoot();
        IsFirstRun = !File.Exists(AppDataPaths.Settings);
        Settings = IsFirstRun ? new DockSettings() : Load();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => Save();

        Settings.PropertyChanged += OnSettingsChanged;
        Settings.PinnedApps.CollectionChanged += OnPinnedAppsChanged;
    }

    public DockSettings Settings { get; }

    public bool IsFirstRun { get; }

    public void Save()
    {
        _saveTimer.Stop();
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        var temporaryPath = AppDataPaths.Settings + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, AppDataPaths.Settings, overwrite: true);
    }

    public void Dispose()
    {
        if (_saveTimer.IsEnabled)
        {
            Save();
        }

        Settings.PropertyChanged -= OnSettingsChanged;
        Settings.PinnedApps.CollectionChanged -= OnPinnedAppsChanged;
    }

    private static DockSettings Load()
    {
        try
        {
            var json = File.ReadAllText(AppDataPaths.Settings);
            return JsonSerializer.Deserialize<DockSettings>(json, JsonOptions) ?? new DockSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new DockSettings();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => ScheduleSave();

    private void OnPinnedAppsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleSave();

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
