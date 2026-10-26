using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using LiquidGlass.Services;
using LiquidGlass.ViewModels;
using LiquidGlass.Views;

namespace LiquidGlass;

[SuppressMessage("Design", "CA1001", Justification = "Os serviços são descartados em OnExit, o fim do ciclo de vida do app.")]
public partial class App : Application
{
    private const string SettingsArgument = "--settings";
    private const string QuitArgument = "--quit";

    private readonly IconService _icons = new();
    private readonly TrayIconService _tray = new();
    private SingleInstance? _instance;
    private SettingsService? _settingsService;
    private TaskbarController? _taskbar;
    private WindowTracker? _tracker;
    private DockViewModel? _dockViewModel;
    private DockWindow? _dockWindow;
    private SettingsWindow? _settingsWindow;
    private int _taskbarCreatedMessage;

    static App()
    {
        // O WPF limita as animações a 60 fps por padrão. Em monitores de 120+ Hz o quique, os
        // rótulos e os painéis engasgavam ao lado da ampliação, que já roda na taxa da tela.
        Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(Timeline),
            new FrameworkPropertyMetadata { DefaultValue = DisplayRefreshRate() });
    }

    private DockSettings Settings => _settingsService!.Settings;

    private static int DisplayRefreshRate()
    {
        var screenDc = User32.GetDC(0);
        try
        {
            // 0 e 1 significam "padrão do hardware", sem valor conhecido.
            var rate = Gdi32.GetDeviceCaps(screenDc, Gdi32.VerticalRefresh);
            return rate > 1 ? rate : 60;
        }
        finally
        {
            User32.ReleaseDC(0, screenDc);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new SingleInstance();
        if (e.Args.Contains(QuitArgument))
        {
            if (!_instance.IsFirstInstance)
            {
                _instance.RequestQuit();
            }

            Shutdown();
            return;
        }

        if (!_instance.IsFirstInstance)
        {
            // Outra dock já está rodando: abrir o app de novo mostra a personalização dela.
            _instance.RequestShowSettings();
            Shutdown();
            return;
        }

        RegisterCrashHandlers();
        _instance.Listen(
            onShowSettings: () => Dispatcher.BeginInvoke(OpenSettings),
            onQuit: () => Dispatcher.BeginInvoke(Shutdown));

        _settingsService = new SettingsService();
        if (_settingsService.IsFirstRun)
        {
            ImportInitialPins();
        }

        _taskbar = new TaskbarController();
        _taskbar.Apply(Settings.TaskbarMode);
        Settings.PropertyChanged += OnSettingsChanged;

        _tray.IsEnabled = Settings.ShowTray;

        _tracker = new WindowTracker();
        _dockViewModel = new DockViewModel(
            Settings,
            _tracker,
            _icons,
            (ImageSource)FindResource("StartIcon"),
            (ImageSource)FindResource("TrayIcon"));
        _dockViewModel.SettingsRequested += (_, _) => OpenSettings();
        _dockViewModel.QuitRequested += (_, _) => Shutdown();

        _dockWindow = new DockWindow(_dockViewModel, new WallpaperService(), _tray);
        _dockWindow.Closed += (_, _) => Shutdown();
        _dockWindow.Show();

        var source = _dockWindow.NativeSource!;
        _tracker.Attach(source);
        _taskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");
        source.AddHook(WndProc);

        if (_settingsService.IsFirstRun || e.Args.Contains(SettingsArgument))
        {
            OpenSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsWindow?.Close();
        _dockWindow?.Close();
        _dockViewModel?.Dispose();
        _tracker?.Dispose();
        _tray.Dispose();
        _taskbar?.Dispose();
        _settingsService?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void ImportInitialPins()
    {
        var pins = PinnedAppFactory.ImportTaskbarPins().ToList();
        foreach (var pin in pins.Count > 0 ? pins : PinnedAppFactory.Defaults())
        {
            Settings.PinnedApps.Add(pin);
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(new SettingsViewModel(Settings, _icons));
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DockSettings.TaskbarMode):
                _taskbar?.Apply(Settings.TaskbarMode);
                break;
            case nameof(DockSettings.ShowTray):
                _tray.IsEnabled = Settings.ShowTray;
                break;
            case nameof(DockSettings.LaunchAtStartup):
                StartupService.SetEnabled(Settings.LaunchAtStartup);
                break;
        }
    }

    /// <summary>O Explorer reiniciou: a barra nativa voltou ao estado padrão e precisa ser escondida de novo.</summary>
    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            _taskbar?.Reapply();
            _tracker?.Refresh();
        }

        return 0;
    }

    /// <summary>
    /// Um erro inesperado nunca pode deixar o usuário sem barra de tarefas: registra o erro,
    /// devolve a barra nativa e encerra.
    /// </summary>
    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            HandleCrash(args.Exception);
            args.Handled = true;
            Shutdown();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) => HandleCrash(args.ExceptionObject as Exception);
    }

    private void HandleCrash(Exception? exception)
    {
        try
        {
            AppDataPaths.EnsureRoot();
            File.AppendAllText(AppDataPaths.CrashLog, $"[{DateTime.Now:O}] {exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Registrar o erro é secundário; restaurar a barra de tarefas não.
        }

        _taskbar?.Restore();
    }
}
