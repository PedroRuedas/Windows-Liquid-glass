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
    private readonly WallpaperService _wallpaper = new();
    private readonly BatteryService _battery = new();
    private readonly NetworkService _network = new();
    private readonly VirtualDesktopService _desktops = new();
    private readonly UpdateService _updates = new();

    /// <summary>Quanto o "--quit" espera a dock em execução devolver a barra de tarefas e encerrar.</summary>
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(10);

    private readonly object _settingsActivity = new();
    private EfficiencyMode? _efficiency;
    private SingleInstance? _instance;
    private SettingsService? _settingsService;
    private TaskbarController? _taskbar;
    private WindowTracker? _tracker;
    private WindowAnimator? _animator;
    private GlobalInputHook? _input;
    private WorkspaceOverview? _overview;
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
            Quit();
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

        // Lido antes de observar as mudanças: refletir o registro não deve regravá-lo.
        Settings.LaunchAtStartup = StartupService.IsEnabled();

        _taskbar = new TaskbarController();
        _taskbar.Apply(Settings.TaskbarMode);
        Settings.PropertyChanged += OnSettingsChanged;
        _efficiency = new EfficiencyMode();

        _tray.IsEnabled = Settings.ShowTray;

        _tracker = new WindowTracker();
        _animator = new WindowAnimator(Settings, _efficiency);
        _network.Start();
        var dockIcons = new DockIcons(
            (ImageSource)FindResource("StartIcon"),
            (ImageSource)FindResource("TrayIcon"),
            (ImageSource)FindResource("WorkspacesIcon"));
        _dockViewModel = new DockViewModel(Settings, _tracker, _icons, _battery, _network, _animator, dockIcons) { Updates = _updates };
        _dockViewModel.SettingsRequested += (_, _) => OpenSettings();
        _dockViewModel.QuitRequested += (_, _) => Shutdown();
        _dockViewModel.UpdatesRequested += (_, _) => ShowUpdates();
        _updates.SetAutomatic(Settings.CheckForUpdates);

        _dockWindow = new DockWindow(_dockViewModel, _wallpaper, _tray, _efficiency);
        _dockWindow.Closed += (_, _) => Shutdown();
        _dockWindow.Show();
        _animator.IconLocator = _dockWindow.FindIconBounds;

        var source = _dockWindow.NativeSource!;
        _tracker.Attach(source);
        _battery.Attach(source);

        _input = new GlobalInputHook();
        _overview = new WorkspaceOverview(CreateOverviewContext, _input, _efficiency, keep => _dockWindow.KeepVisible = keep);
        _input.OverviewKeyPressed += (_, _) => _overview.Toggle();
        _input.SwitchKeyPressed += (_, direction) => _overview.Switch(direction);
        _input.OverviewNavigation += (_, key) => _overview.HandleKey(key);
        _input.CaptionButtonPressed += (_, pressed) => _animator.OnCaptionButtonPressed(pressed.Window, pressed.Button);
        _dockViewModel.WorkspacesRequested += (_, _) => _overview.Toggle();
        _dockViewModel.ItemActivating += (_, item) =>
        {
            if (item.Kind != DockItemKind.WorkspacesButton)
            {
                _overview.Close();
            }
        };
        ApplyWindowFeatures();
        StartDemos();
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
        _updates.Dispose();
        _overview?.Dispose();
        _input?.Dispose();
        _animator?.Dispose();
        _dockWindow?.Close();
        _dockViewModel?.Dispose();
        _battery.Dispose();
        _network.Dispose();
        _tracker?.Dispose();
        _tray.Dispose();
        _efficiency?.Dispose();
        _taskbar?.Dispose();
        _settingsService?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// "--quit": pede à dock em execução que encerre e espera ela devolver a barra de tarefas.
    /// Sem dock em execução, ainda restaura uma barra que ficou escondida por um encerramento
    /// forçado. O desinstalador depende disso para nunca deixar o usuário sem barra.
    /// </summary>
    private void Quit()
    {
        if (!_instance!.IsFirstInstance)
        {
            _instance.RequestQuit();
            _instance.WaitForFirstInstanceToExit(QuitTimeout);
        }

        // Construir o controlador recupera um estado salvo; descartá-lo restaura o que sobrou.
        new TaskbarController().Dispose();

        Shutdown();
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

        _settingsWindow = new SettingsWindow(new SettingsViewModel(Settings, _icons, _updates));
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _efficiency?.SetActive(_settingsActivity, active: false);
        };
        _efficiency?.SetActive(_settingsActivity, active: true);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Personalização aberta nas atualizações; sem versão nova conhecida, procura uma.</summary>
    private void ShowUpdates()
    {
        OpenSettings();
        _settingsWindow?.ShowUpdates();
        if (_updates.Available is null)
        {
            _ = _updates.CheckAsync(userInitiated: true);
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DockSettings.CheckForUpdates):
                _updates.SetAutomatic(Settings.CheckForUpdates);
                break;
            case nameof(DockSettings.TaskbarMode):
                _taskbar?.Apply(Settings.TaskbarMode);
                break;
            case nameof(DockSettings.ShowTray):
                _tray.IsEnabled = Settings.ShowTray;
                break;
            case nameof(DockSettings.LaunchAtStartup):
                StartupService.SetEnabled(Settings.LaunchAtStartup);
                break;
            case nameof(DockSettings.AnimateWindows):
            case nameof(DockSettings.AnimateWorkspaceSwitch):
            case nameof(DockSettings.WinTabOpensOverview):
                ApplyWindowFeatures();
                break;
        }
    }

    /// <summary>Liga só os ganchos que as opções escolhidas usam.</summary>
    private void ApplyWindowFeatures()
    {
        if (_input is null || _animator is null)
        {
            return;
        }

        _animator.IsEnabled = Settings.AnimateWindows;
        _input.WatchCaptionButtons = Settings.AnimateWindows;
        _input.InterceptWinTab = Settings.WinTabOpensOverview;

        // Sem a troca de áreas desta versão do Windows, Ctrl+Win+setas ficam com o próprio Windows.
        _input.InterceptDesktopSwitch = Settings.AnimateWorkspaceSwitch && _desktops.IsAvailable;
    }

    /// <summary>
    /// Só no Debug, para testar sem teclado: LIQUIDGLASS_CUBE_DEMO=1 abre o cubo, gira-o como num
    /// arrasto e salva quadros; LIQUIDGLASS_DRAG_DEMO com o título de uma janela abre o cubo e a arrasta
    /// até a área à direita; LIQUIDGLASS_GENIE_DEMO com o título de uma janela a minimiza e restaura.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void StartDemos()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        var step = 0;
        var cubeDemo = Environment.GetEnvironmentVariable("LIQUIDGLASS_CUBE_DEMO") == "1";
        var dragTitle = Environment.GetEnvironmentVariable("LIQUIDGLASS_DRAG_DEMO");
        timer.Tick += (_, _) =>
        {
            step++;
            if (dragTitle is { Length: > 0 })
            {
                switch (step)
                {
                    case 1:
                        _overview?.Toggle();
                        break;
                    case 2:
                        _overview?.Current?.DemoGrab(User32.FindWindow(null, dragTitle));
                        break;
                    case 3:
                        _overview?.Current?.SaveDemoFrame("drag-grab");
                        _overview?.Current?.DemoRotate(back: false);
                        break;
                    case 4:
                        _overview?.Current?.DemoPointAtFront();
                        break;
                    case 5:
                        _overview?.Current?.SaveDemoFrame("drag-over");
                        _overview?.Current?.DemoDrop();
                        break;
                    case 6:
                        _overview?.Current?.SaveDemoFrame("drag-dropped");
                        _overview?.Current?.DemoRotate(back: true);
                        break;
                    case 7:
                        _overview?.Current?.SaveDemoFrame("drag-back");
                        _overview?.Toggle();
                        timer.Stop();
                        break;
                }

                return;
            }

            if (cubeDemo)
            {
                switch (step)
                {
                    case 1:
                        _overview?.Toggle();
                        break;
                    case 2:
                        _overview?.Current?.SaveDemoFrame("cube-open");

                        // Uma face para a esquerda: a face "+" (o anel tem uma face a mais que as áreas).
                        _overview?.Current?.DemoTurn(_overview.Current.DemoFaceStep, 18);
                        break;
                    case 3:
                        _overview?.Current?.SaveDemoFrame("cube-plus");
                        break;
                    case 4:
                        // Na face "+", fechar volta para a área atual sem criar outra.
                        _overview?.Toggle();
                        break;
                }

                if (step >= 4)
                {
                    timer.Stop();
                }

                return;
            }

            if (Environment.GetEnvironmentVariable("LIQUIDGLASS_GENIE_DEMO") is { Length: > 0 } title)
            {
                var hwnd = User32.FindWindow(null, title);
                if (step == 1)
                {
                    _animator?.Minimize(hwnd);
                }
                else if (step == 2)
                {
                    _animator?.Activate(hwnd);
                }
            }

            if (step >= 2)
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    private OverviewContext CreateOverviewContext()
    {
        return new OverviewContext(
            _desktops,
            _tracker!,
            _wallpaper,
            _dockWindow!.NativeSource!.Handle,
            _dockWindow.OverviewReserve(),
            VisualTreeHelper.GetDpi(_dockWindow).DpiScaleX,
            Settings.AnimationSpeed);
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
