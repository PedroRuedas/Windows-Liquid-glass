using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using LiquidGlass.Resources;
using LiquidGlass.Services;
using LiquidGlass.ViewModels;
using LiquidGlass.Views;

namespace LiquidGlass;

[SuppressMessage("Design", "CA1001", Justification = "Os serviços são descartados em OnExit, o fim do ciclo de vida do app.")]
public partial class App : Application
{
    private const string SettingsArgument = "--settings";
    private const string QuitArgument = "--quit";

    /// <summary>Aberta pela própria dock ao reiniciar: espera a anterior fechar em vez de desistir.</summary>
    private const string RestartArgument = "--restart";

    private readonly IconService _icons = new();
    private readonly TrayIconService _tray = new();
    private readonly WallpaperService _wallpaper = new();
    private readonly BatteryService _battery = new();
    private readonly NetworkService _network = new();
    private readonly VirtualDesktopService _desktops = new();
    private readonly UpdateService _updates = new();
    private readonly TaskbarListService _taskbarList = new();

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

    /// <summary>As docks dos outros monitores, com "Dock em todos os monitores" ligado.</summary>
    private readonly List<DockWindow> _extraDocks = [];
    private DispatcherTimer? _displayChangeDelay;
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

        // Só espera quem não é a primeira: esperar um mutex que já é nosso o adquiriria duas vezes.
        var waitedForRestart = !_instance.IsFirstInstance && e.Args.Contains(RestartArgument) && _instance.WaitForFirstInstanceToExit(QuitTimeout);
        if (!_instance.IsFirstInstance && !waitedForRestart)
        {
            // Outra dock já está rodando: abrir o app de novo mostra a personalização dela.
            _instance.RequestShowSettings();
            Shutdown();
            return;
        }

        RegisterCrashHandlers();
        JumpListService.DeleteStaleLinks();
        _instance.Listen(
            onShowSettings: () => Dispatcher.BeginInvoke(OpenSettings),
            onQuit: () => Dispatcher.BeginInvoke(Shutdown));

        _settingsService = new SettingsService();

        // Antes de qualquer janela: os textos são lidos uma vez, ao criar cada uma.
        Strings.Apply(Settings.Language);
        if (_settingsService.IsFirstRun)
        {
            ImportInitialPins();
        }

        // Lido antes de observar as mudanças: refletir o registro não deve regravá-lo.
        Settings.LaunchAtStartup = StartupService.IsEnabled();

        _taskbar = new TaskbarController();
        _taskbar.Apply(Settings.TaskbarMode);
        TaskbarListService.RecoverAfterCrash();
        _taskbarList.OpenWindows = () => _tracker?.Windows.Select(window => window.Handle) ?? [];
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
        _dockViewModel = new DockViewModel(Settings, _tracker, _icons, _battery, _network, _animator, dockIcons) { Updates = _updates, TaskbarList = _taskbarList, Media = new MediaService() };
        _dockViewModel.SettingsRequested += (_, _) => OpenSettings();
        _dockViewModel.QuitRequested += (_, _) => Shutdown();
        _dockViewModel.UpdatesRequested += (_, _) => ShowUpdates();
        _updates.SetAutomatic(Settings.CheckForUpdates);

        _dockWindow = new DockWindow(_dockViewModel, _wallpaper, _tray, _efficiency);
        _dockWindow.Closed += (_, _) => Shutdown();
        _dockWindow.Show();
        _animator.IconLocator = FindIconBounds;

        var source = _dockWindow.NativeSource!;
        _tracker.Attach(source);
        _battery.Attach(source);

        _input = new GlobalInputHook();
        _overview = new WorkspaceOverview(CreateOverviewContext, _input, _efficiency, keep =>
        {
            foreach (var dock in AllDocks())
            {
                dock.KeepVisible = keep;
            }
        });
        _input.OverviewKeyPressed += (_, _) => _overview.Toggle();

        // Sem a dock na tela, a tecla Windows abre o menu do próprio Windows.
        _input.StartKeyPressed += (_, _) =>
        {
            if (!DockAtCursor().ToggleStartMenu())
            {
                AppLauncher.OpenStartMenu();
            }
        };
        _input.SwitchKeyPressed += (_, direction) => _overview.Switch(direction);
        _input.AppKeyPressed += (_, key) => _dockViewModel.ActivateByNumber(key.Index, key.NewWindow);
        _input.OverviewNavigation += (_, key) => _overview.HandleKey(key);
        _input.CaptionButtonPressed += (_, pressed) => _animator.OnCaptionButtonPressed(pressed.Window, pressed.Button);
        _input.EscapePressed += (_, _) =>
        {
            foreach (var dock in AllDocks())
            {
                dock.CloseWindowPreview();
            }
        };
        _dockWindow.WindowPreviewOpenChanged += (_, isOpen) => _input.WatchEscape = isOpen;
        _dockViewModel.WorkspacesRequested += (_, _) => _overview.Toggle();
        _dockViewModel.ItemActivating += (_, item) =>
        {
            if (item.Kind != DockItemKind.WorkspacesButton)
            {
                _overview.Close();
            }
        };
        ApplyWindowFeatures();
        UpdateExtraDocks();

        // Depois do rastreador de janelas: ao começar, a dock pede aos apps abertos o selo e o progresso.
        ApplyTaskbarList();
#if DEBUG
        StartDemos();
#endif
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
        foreach (var dock in _extraDocks)
        {
            dock.Close();
        }

        _extraDocks.Clear();
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
        _taskbarList.Dispose();
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
        TaskbarListService.RecoverAfterCrash();

        Shutdown();
    }

    /// <summary>
    /// Abre outra dock, que espera esta fechar e assume com a personalização aberta. Se ela não
    /// abrir, esta continua: nunca deixa o usuário sem dock nem barra de tarefas.
    /// </summary>
    private void Restart()
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable)
            {
                ArgumentList = { RestartArgument, SettingsArgument },
                UseShellExecute = false,
            });
        }
        catch (Win32Exception)
        {
            return;
        }

        Shutdown();
    }

    /// <summary>
    /// Os selos e o progresso dos apps só vêm para a dock com a barra do Windows oculta: visível, ela
    /// continua mostrando-os, e tirar isso dela seria pior.
    /// </summary>
    private void ApplyTaskbarList() =>
        _taskbarList.SetEnabled(Settings.ShowBadges && Settings.TaskbarMode == TaskbarMode.Hidden);

    private const int WmDisplayChange = 0x007E;

    private IEnumerable<DockWindow> AllDocks() => _dockWindow is null ? _extraDocks : _extraDocks.Prepend(_dockWindow);

    /// <summary>
    /// Com "Dock em todos os monitores", uma dock em cada monitor além do principal. Refeitas do zero
    /// quando a opção ou as telas mudam: monitores aparecem e somem com a mesma facilidade.
    /// </summary>
    private void UpdateExtraDocks()
    {
        foreach (var dock in _extraDocks)
        {
            dock.Close();
        }

        _extraDocks.Clear();
        if (!Settings.ShowOnAllMonitors || _dockViewModel is null || _efficiency is null)
        {
            return;
        }

        foreach (var screen in System.Windows.Forms.Screen.AllScreens.Where(screen => !screen.Primary))
        {
            var (x, y) = (screen.Bounds.Left + (screen.Bounds.Width / 2), screen.Bounds.Top + (screen.Bounds.Height / 2));
            User32.MonitorInfo Monitor() => User32.GetMonitorInfoAt(x, y);
            var dock = new DockWindow(_dockViewModel, new WallpaperService(Monitor, isPrimary: false), _tray, _efficiency, Monitor);
            dock.WindowPreviewOpenChanged += (_, isOpen) => _input!.WatchEscape = isOpen;
            _extraDocks.Add(dock);
            dock.Show();
        }
    }

    /// <summary>A lâmpada mágica vai até o ícone da dock que está no monitor da janela.</summary>
    private Rect? FindIconBounds(nint hwnd)
    {
        var monitor = User32.GetMonitorInfoForWindow(hwnd).Monitor;
        var dock = _extraDocks.FirstOrDefault(candidate => SameRect(candidate.MonitorBounds, monitor)) ?? _dockWindow;
        return dock?.FindIconBounds(hwnd) ?? _dockWindow?.FindIconBounds(hwnd);
    }

    /// <summary>A tecla Windows abre o menu Iniciar na dock do monitor em que está o mouse.</summary>
    private DockWindow DockAtCursor()
    {
        if (User32.GetCursorPos(out var cursor))
        {
            var monitor = User32.GetMonitorInfoAt(cursor.X, cursor.Y).Monitor;
            if (_extraDocks.FirstOrDefault(candidate => SameRect(candidate.MonitorBounds, monitor)) is { } dock)
            {
                return dock;
            }
        }

        return _dockWindow!;
    }

    private static bool SameRect(User32.Rect a, User32.Rect b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

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

        var viewModel = new SettingsViewModel(Settings, _icons, _updates);
        viewModel.RestartRequested += (_, _) => Restart();
        _settingsWindow = new SettingsWindow(viewModel);
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
                ApplyTaskbarList();
                break;
            case nameof(DockSettings.ShowBadges):
                ApplyTaskbarList();
                break;
            case nameof(DockSettings.ShowTray):
                _tray.IsEnabled = Settings.ShowTray;
                break;
            case nameof(DockSettings.ShowOnAllMonitors):
                UpdateExtraDocks();
                break;
            case nameof(DockSettings.LaunchAtStartup):
                StartupService.SetEnabled(Settings.LaunchAtStartup);
                break;
            case nameof(DockSettings.AnimateWindows):
            case nameof(DockSettings.AnimateWorkspaceSwitch):
            case nameof(DockSettings.WinTabOpensOverview):
            case nameof(DockSettings.UseGlassStartMenu):
            case nameof(DockSettings.WinNumberKeys):
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
        _input.InterceptStartKey = Settings.UseGlassStartMenu;
        _input.InterceptAppKeys = Settings.WinNumberKeys;

        // Sem a troca de áreas desta versão do Windows, Ctrl+Win+setas ficam com o próprio Windows.
        _input.InterceptDesktopSwitch = Settings.AnimateWorkspaceSwitch && _desktops.IsAvailable;
    }

#if DEBUG
    /// <summary>
    /// Só no Debug, para testar sem teclado: LIQUIDGLASS_CUBE_DEMO=1 abre o cubo, gira-o como num
    /// arrasto e salva quadros; LIQUIDGLASS_DRAG_DEMO com o título de uma janela abre o cubo e a arrasta
    /// até a área à direita; LIQUIDGLASS_GENIE_DEMO com o título de uma janela a minimiza e restaura;
    /// LIQUIDGLASS_PREVIEW_DEMO com o nome de um app aberto mostra as miniaturas dele e as fotografa;
    /// LIQUIDGLASS_MENU_DEMO com o nome de um app abre o menu do botão direito dele e o fotografa;
    /// LIQUIDGLASS_REORDER_DEMO com o nome de um app o arrasta LIQUIDGLASS_REORDER_SLOTS vagas (2,2 se
    /// ausente; negativo para trás), fotografa e desfaz; LIQUIDGLASS_START_DEMO com um termo de arquivo
    /// abre o menu Iniciar de vidro, pesquisa um app, uma configuração e esse termo e fotografa cada passo;
    /// LIQUIDGLASS_SIZE_DEMO com tamanhos de ícone ("16,24,32") fotografa a dock em cada um e volta ao original.
    /// </summary>
    private void StartDemos()
    {
        if (Environment.GetEnvironmentVariable("LIQUIDGLASS_SHOWCASE_DEMO") is { Length: > 0 } showcaseTitle)
        {
            StartShowcase(showcaseTitle);
            return;
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        var step = 0;
        var cubeDemo = Environment.GetEnvironmentVariable("LIQUIDGLASS_CUBE_DEMO") == "1";
        var dragTitle = Environment.GetEnvironmentVariable("LIQUIDGLASS_DRAG_DEMO");
        var reorderOriginal = -1;
        var sizeOriginal = 0.0;
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

            if (Environment.GetEnvironmentVariable("LIQUIDGLASS_START_DEMO") is { Length: > 0 } fileQuery && _dockWindow is { } dock)
            {
                var menu = dock.DemoStartMenu;
                switch (step)
                {
                    case 1:
                        dock.ToggleStartMenu();
                        break;
                    case 2:
                        menu.SaveDemo("start-home");
                        menu.DemoPowerMenu(open: true);
                        break;
                    case 3:
                        // Só fotografa o menu de energia; nenhum item é clicado.
                        menu.SaveDemo("start-power");
                        menu.DemoPowerMenu(open: false);
                        menu.DemoSearch("calc");
                        break;
                    case 4:
                        menu.SaveDemo("start-search-app");
                        menu.DemoSearch("wifi");
                        break;
                    case 5:
                        menu.SaveDemo("start-search-setting");
                        menu.DemoSearch(fileQuery);
                        break;
                    case 7:
                        // Os arquivos vêm do índice do Windows: um passo a mais de espera.
                        menu.SaveDemo("start-search-files");
                        dock.ToggleStartMenu();
                        break;
                    case 8:
                        menu.SaveDemo("start-closed");
                        timer.Stop();
                        break;
                }

                return;
            }

            if (Environment.GetEnvironmentVariable("LIQUIDGLASS_REORDER_DEMO") is { Length: > 0 } reorderApp)
            {
                switch (step)
                {
                    case 1:
                        _dockWindow?.SaveDemoDock("reorder-before");
                        var slots = double.TryParse(Environment.GetEnvironmentVariable("LIQUIDGLASS_REORDER_SLOTS"), System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 2.2;
                        reorderOriginal = _dockWindow?.DemoReorder(reorderApp, slots) ?? -1;
                        break;
                    case 2:
                        _dockWindow?.SaveDemoDock("reorder-drag");
                        _dockWindow?.DemoDrop(reorderApp, restoreTo: -1);
                        break;
                    case 3:
                        // A ordem nova vale; depois o demo a desfaz para não mexer nos fixados do usuário.
                        _dockWindow?.SaveDemoDock("reorder-dropped");
                        _dockWindow?.DemoDrop(reorderApp, reorderOriginal);
                        break;
                    case 4:
                        _dockWindow?.SaveDemoDock("reorder-restored");
                        timer.Stop();
                        break;
                }

                return;
            }

            if (Environment.GetEnvironmentVariable("LIQUIDGLASS_SIZE_DEMO") is { Length: > 0 } sizeList && _dockWindow is { } sizedDock)
            {
                var sizes = sizeList.Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (step == 1)
                {
                    sizeOriginal = Settings.IconSize;
                }

                // Um passo para aplicar cada tamanho e o seguinte para fotografar; no fim volta ao original.
                var index = (step - 1) / 2;
                if (index < sizes.Length)
                {
                    if ((step - 1) % 2 == 0)
                    {
                        Settings.IconSize = sizes[index];
                    }
                    else
                    {
                        sizedDock.SaveDemoDock($"size-{sizes[index]:0}");
                    }
                }
                else
                {
                    Settings.IconSize = sizeOriginal;
                    timer.Stop();
                }

                return;
            }

            if (Environment.GetEnvironmentVariable("LIQUIDGLASS_MENU_DEMO") is { Length: > 0 } menuApp)
            {
                // O menu é fotografado ao fechar, um passo depois de aberto.
                _dockWindow?.DemoMenu(step == 1 ? menuApp : null);
                if (step >= 2)
                {
                    timer.Stop();
                }

                return;
            }

            if (Environment.GetEnvironmentVariable("LIQUIDGLASS_STACK_DEMO") is { Length: > 0 } folderName)
            {
                // A pasta fixada com esse nome abre como pilha, pelo mesmo caminho do clique, e é fotografada.
                if (step == 1)
                {
                    var folder = _dockViewModel?.Items.FirstOrDefault(item => item.Name.Contains(folderName, StringComparison.OrdinalIgnoreCase));
                    Graphics.DemoCapture.Log($"pilha: item {folder?.Name ?? "não encontrado"}, alvo {folder?.Pin?.Target}");
                    if (folder is not null)
                    {
                        _dockWindow?.HoldDemoStack();
                        _dockViewModel!.Activate(folder);
                    }
                }
                else if (step == 2)
                {
                    _dockWindow?.SaveDemoStack("stack-open");
                    timer.Stop();
                }

                return;
            }

            if (Environment.GetEnvironmentVariable("LIQUIDGLASS_PREVIEW_DEMO") is { Length: > 0 } app)
            {
                switch (step)
                {
                    case 0:
                        break;
                    case 1:
                        // Como o mouse parado faria: primeiro descobre o que está tocando.
                        _ = _dockViewModel?.Media?.RefreshAsync().ContinueWith(
                            refresh => Dispatcher.Invoke(() =>
                            {
                                Graphics.DemoCapture.Log($"mídia: {refresh.Status}; apps: {string.Join(", ", _dockViewModel.Items.Where(item => item.IsRunning).Select(item => item.Name))}");
                                _dockWindow?.DemoPreview(app);
                            }),
                            TaskScheduler.Default);
                        break;
                    case 2:
                        // Solta o painel: com o mouse longe, ele recolhe sozinho para dentro do ícone.
                        _dockWindow?.SaveDemoPreview("preview-open");
                        _dockWindow?.DemoReleasePreview();
                        break;
                    case 3:
                        _dockWindow?.DemoPreview(null);
                        timer.Stop();
                        break;
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

    /// <summary>
    /// Demonstração (Debug) para o GIF do README, com LIQUIDGLASS_SHOWCASE_DEMO = título de uma
    /// janela: o "mouse" passa pela dock e volta (ampliação), sai, e a janela minimiza em lâmpada
    /// mágica e volta. Tudo por código, sem mexer no mouse nem no teclado. Começa 6 s depois de
    /// abrir e registra os marcos no demo.log. Usa ícones e ampliação maiores durante a gravação e
    /// devolve os valores do usuário no fim.
    /// </summary>
    private void StartShowcase(string title)
    {
        const double Start = 6;
        var (iconSize, magnification) = (Settings.IconSize, Settings.Magnification);
        Settings.IconSize = 52;
        Settings.Magnification = 1.8;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var minimized = false;
        var restored = false;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        timer.Tick += (_, _) =>
        {
            var t = clock.Elapsed.TotalSeconds - Start;
            if (t < 0)
            {
                return;
            }

            if (t < 3.2)
            {
                // Vai do começo ao fim da dock, devagar nas pontas.
                _dockWindow?.DemoHover(0.04 + (0.92 * Ease(t / 3.2)));
            }
            else if (t < 4.8)
            {
                // Volta até um terço e para.
                _dockWindow?.DemoHover(0.96 - (0.62 * Ease((t - 3.2) / 1.6)));
            }
            else if (t < 5.6)
            {
                _dockWindow?.DemoHover(0.34);
            }
            else if (!minimized)
            {
                _dockWindow?.DemoHover(null);
                minimized = true;
                Graphics.DemoCapture.Log("showcase: minimizar");
                _animator?.Minimize(User32.FindWindow(null, title));
            }
            else if (t >= 8.2 && !restored)
            {
                restored = true;
                Graphics.DemoCapture.Log("showcase: restaurar");
                _animator?.Activate(User32.FindWindow(null, title));
            }
            else if (t >= 10.5)
            {
                Settings.IconSize = iconSize;
                Settings.Magnification = magnification;
                Graphics.DemoCapture.Log("showcase: fim");
                timer.Stop();
            }
        };
        Graphics.DemoCapture.Log("showcase: aberto");
        timer.Start();

        static double Ease(double x) => (1 - Math.Cos(Math.PI * Math.Clamp(x, 0, 1))) / 2;
    }
#endif

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
        if (message == WmDisplayChange)
        {
            // Monitor ligado, desligado ou com outra resolução: as docks extras são refeitas depois
            // que o Windows termina de reorganizar as telas.
            _displayChangeDelay ??= new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) =>
            {
                _displayChangeDelay?.Stop();
                UpdateExtraDocks();
            }, Dispatcher);
            _displayChangeDelay.Stop();
            _displayChangeDelay.Start();
        }

        if (message == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            _taskbar?.Reapply();
            _taskbarList.OnTaskbarCreated();
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
        _taskbarList.SetEnabled(false);
    }
}
