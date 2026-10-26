using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Media;
using LiquidGlass.Services;
using LiquidGlass.ViewModels;
using LiquidGlass.Views;

namespace LiquidGlass;

[SuppressMessage("Design", "CA1001", Justification = "Os serviços são descartados em OnExit, o fim do ciclo de vida do app.")]
public partial class App : Application
{
    private SettingsService? _settingsService;
    private WindowTracker? _tracker;
    private DockViewModel? _dockViewModel;
    private DockWindow? _dockWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService = new SettingsService();
        var settings = _settingsService.Settings;
        if (_settingsService.IsFirstRun)
        {
            var pins = PinnedAppFactory.ImportTaskbarPins().ToList();
            foreach (var pin in pins.Count > 0 ? pins : PinnedAppFactory.Defaults())
            {
                settings.PinnedApps.Add(pin);
            }
        }

        _tracker = new WindowTracker();
        _dockViewModel = new DockViewModel(settings, _tracker, new IconService(), (ImageSource)FindResource("StartIcon"));
        _dockViewModel.QuitRequested += (_, _) => Shutdown();

        _dockWindow = new DockWindow(_dockViewModel, new WallpaperService());
        _dockWindow.Show();
        _tracker.Attach(_dockWindow.NativeSource!);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _dockWindow?.Close();
        _dockViewModel?.Dispose();
        _tracker?.Dispose();
        _settingsService?.Dispose();
        base.OnExit(e);
    }
}
