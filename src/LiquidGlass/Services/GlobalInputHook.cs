using System.Runtime.InteropServices;
using System.Windows.Threading;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>Tecla da visão das áreas de trabalho repassada enquanto ela está aberta.</summary>
public enum OverviewKey
{
    Escape,
    Left,
    Right,
    Enter,
}

/// <summary>Botão da barra de título pressionado em uma janela de outro app.</summary>
public enum CaptionButton
{
    Minimize,
    Close,
}

/// <summary>
/// Ganchos globais de teclado e mouse. Rodam numa thread própria, de prioridade alta e fora do
/// modo de eficiência: o Windows espera cada gancho antes de entregar a tecla ou o clique ao app,
/// então qualquer atraso aqui seria sentido no sistema inteiro. Os eventos chegam na thread da
/// interface.
/// <list type="bullet">
/// <item>Win+Tab abre a visão das áreas de trabalho; Ctrl+Win+setas trocam de área com animação.</item>
/// <item>Com a visão aberta, Esc, setas e Enter vão para ela.</item>
/// <item>Com as miniaturas abertas, Esc as fecha sem tirar a tecla do app em foco.</item>
/// <item>Clique em minimizar ou fechar de qualquer janela avisa antes de a janela reagir, a tempo
/// de desligar a animação nativa e tocar a da dock.</item>
/// </list>
/// </summary>
public sealed class GlobalInputHook : IDisposable
{
    private const int UpdateHooksMessage = User32.WmApp + 1;
    private const uint HitTestTimeout = 40;
    private const uint HookRefreshInterval = 30_000;
    private const uint VkLeftControl = 0xA2;
    private const uint VkRightControl = 0xA3;
    private const uint VkLeftAlt = 0xA4;
    private const uint VkRightAlt = 0xA5;

    /// <summary>Marca as teclas que a própria dock injeta, para o gancho não reagir a elas.</summary>
    private const nint OwnInputMarker = 0x4C47_4443;

    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;
    private readonly User32.LowLevelHookProc _keyboardProc;
    private readonly User32.LowLevelHookProc _mouseProc;
    private readonly HashSet<uint> _swallowedKeys = [];
    private readonly ManualResetEventSlim _started = new();
    private nint _keyboardHook;
    private nint _mouseHook;
    private uint _threadId;
    private bool _swallowedWithWindowsKey;
    private bool _windowsDown;
    private bool _controlDown;
    private bool _altDown;

    private volatile bool _interceptWinTab;
    private volatile bool _interceptDesktopSwitch;
    private volatile bool _captureOverviewKeys;
    private volatile bool _watchCaptionButtons;
    private volatile bool _watchEscape;

    public GlobalInputHook()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _keyboardProc = OnKeyboard;
        _mouseProc = OnMouse;
        _thread = new Thread(Run) { IsBackground = true, Name = "Ganchos de entrada", Priority = ThreadPriority.Highest };
        _thread.Start();
        _started.Wait();
    }

    public event EventHandler? OverviewKeyPressed;

    /// <summary>Ctrl+Win+seta: -1 para a área da esquerda, +1 para a da direita.</summary>
    public event EventHandler<int>? SwitchKeyPressed;

    public event EventHandler<OverviewKey>? OverviewNavigation;

    public event EventHandler<(nint Window, CaptionButton Button)>? CaptionButtonPressed;

    /// <summary>Esc apertado com <see cref="WatchEscape"/> ligado. A tecla segue para o app em foco.</summary>
    public event EventHandler? EscapePressed;

    public bool InterceptWinTab
    {
        get => _interceptWinTab;
        set
        {
            if (_interceptWinTab != value)
            {
                _interceptWinTab = value;
                RequestHookUpdate();
            }
        }
    }

    public bool InterceptDesktopSwitch
    {
        get => _interceptDesktopSwitch;
        set
        {
            if (_interceptDesktopSwitch != value)
            {
                _interceptDesktopSwitch = value;
                RequestHookUpdate();
            }
        }
    }

    public bool CaptureOverviewKeys
    {
        get => _captureOverviewKeys;
        set
        {
            if (_captureOverviewKeys != value)
            {
                _captureOverviewKeys = value;
                RequestHookUpdate();
            }
        }
    }

    /// <summary>Avisa de Esc sem consumi-lo: os painéis da dock não tiram o foco do app para ouvir o teclado.</summary>
    public bool WatchEscape
    {
        get => _watchEscape;
        set
        {
            if (_watchEscape != value)
            {
                _watchEscape = value;
                RequestHookUpdate();
            }
        }
    }

    public bool WatchCaptionButtons
    {
        get => _watchCaptionButtons;
        set
        {
            if (_watchCaptionButtons != value)
            {
                _watchCaptionButtons = value;
                RequestHookUpdate();
            }
        }
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            User32.PostThreadMessage(_threadId, User32.WmQuit, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(1));
            _threadId = 0;
        }

        _started.Dispose();
    }

#if DEBUG
    /// <summary>Teste: LIQUIDGLASS_HOOK_TRACE=1 grava as teclas vistas pelo gancho em hook.log.</summary>
    private static readonly bool TraceEnabled = Environment.GetEnvironmentVariable("LIQUIDGLASS_HOOK_TRACE") == "1";
#else
    private static bool TraceEnabled => false;
#endif

    [System.Diagnostics.Conditional("DEBUG")]
    private static void Trace(string message)
    {
        if (TraceEnabled)
        {
            File.AppendAllText(Path.Combine(AppDataPaths.Root, "hook.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
    }

    private static bool IsWindowsKey(uint key) => key is User32.VkLeftWin or User32.VkRightWin;

    private static bool IsWindowsKeyDown() => User32.IsKeyDown(User32.VkLeftWin) || User32.IsKeyDown(User32.VkRightWin);

    /// <summary>Os ganchos só ficam instalados enquanto alguma função precisa deles.</summary>
    private void RequestHookUpdate()
    {
        if (_threadId != 0)
        {
            User32.PostThreadMessage(_threadId, UpdateHooksMessage, 0, 0);
        }
    }

    private void Run()
    {
        Kernel32.ExcludeCurrentThreadFromEfficiencyMode();

        // A fila de mensagens só existe depois da primeira chamada que a usa. Sem ela, os pedidos
        // de instalar os ganchos feitos logo após o construtor se perderiam.
        User32.PeekMessage(out _, 0, 0, 0, 0);
        _threadId = Kernel32.GetCurrentThreadId();
        _started.Set();

        UpdateHooks();
        var refreshTimer = User32.SetTimer(0, 0, HookRefreshInterval, 0);
        while (User32.GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Id == UpdateHooksMessage)
            {
                UpdateHooks();
            }
            else if (message.Id == User32.WmTimer)
            {
                RefreshHooks();
            }
        }

        User32.KillTimer(0, refreshTimer);

        Unhook(ref _keyboardHook);
        Unhook(ref _mouseHook);
    }

    private void UpdateHooks()
    {
        var module = Kernel32.GetModuleHandle(null);
        var wantsKeyboard = _interceptWinTab || _interceptDesktopSwitch || _captureOverviewKeys || _watchEscape;
        if (wantsKeyboard && _keyboardHook == 0)
        {
            _keyboardHook = User32.SetWindowsHookEx(User32.WhKeyboardLl, _keyboardProc, module, 0);
            Trace($"gancho de teclado: {_keyboardHook} (erro {Marshal.GetLastWin32Error()})");
        }
        else if (!wantsKeyboard)
        {
            Unhook(ref _keyboardHook);
        }

        if (_watchCaptionButtons && _mouseHook == 0)
        {
            _mouseHook = User32.SetWindowsHookEx(User32.WhMouseLl, _mouseProc, module, 0);
        }
        else if (!_watchCaptionButtons)
        {
            Unhook(ref _mouseHook);
        }
    }

    /// <summary>
    /// O Windows descarta em silêncio um gancho que demorou a responder uma vez (por exemplo, com o
    /// processo pausado por uma coleta de lixo) e não avisa. Reinstalar de tempos em tempos traz o
    /// atalho de volta sozinho; o novo entra antes de o antigo sair, sem intervalo descoberto.
    /// </summary>
    private void RefreshHooks()
    {
        var module = Kernel32.GetModuleHandle(null);
        if (_keyboardHook != 0)
        {
            var previous = _keyboardHook;
            _keyboardHook = User32.SetWindowsHookEx(User32.WhKeyboardLl, _keyboardProc, module, 0);
            User32.UnhookWindowsHookEx(previous);
        }

        if (_mouseHook != 0)
        {
            var previous = _mouseHook;
            _mouseHook = User32.SetWindowsHookEx(User32.WhMouseLl, _mouseProc, module, 0);
            User32.UnhookWindowsHookEx(previous);
        }
    }

    private static void Unhook(ref nint hook)
    {
        if (hook != 0)
        {
            User32.UnhookWindowsHookEx(hook);
            hook = 0;
        }
    }

    private nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && HandleKey((int)wParam, Marshal.PtrToStructure<User32.KeyboardHookData>(lParam)))
        {
            return 1;
        }

        return User32.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    /// <summary>Devolve <c>true</c> quando a tecla é consumida pela dock (o app em foco não a recebe).</summary>
    private bool HandleKey(int message, User32.KeyboardHookData key)
    {
        Trace($"tecla {key.VirtualKey:X2} msg {message:X} flags {key.Flags:X} extra {key.ExtraInfo:X}");

        // Só as teclas que a própria dock injeta são ignoradas. Teclas "simuladas" em geral não:
        // AnyDesk, controle remoto e alguns teclados entregam tudo o que o usuário digita assim.
        if (key.ExtraInfo == OwnInputMarker)
        {
            return false;
        }

        var isDown = message is User32.WmKeyDown or User32.WmSysKeyDown;
        TrackModifier(key.VirtualKey, isDown);
        if (!isDown)
        {
            if (IsWindowsKey(key.VirtualKey) && _swallowedWithWindowsKey)
            {
                // A tecla Windows solta sozinha abriria o menu Iniciar: uma tecla sem função no
                // meio faz o Windows entender que ela fez parte de um atalho.
                _swallowedWithWindowsKey = false;
                User32.keybd_event(User32.VkDummy, 0, 0, OwnInputMarker);
                User32.keybd_event(User32.VkDummy, 0, User32.KeyEventKeyUp, OwnInputMarker);
            }

            return _swallowedKeys.Remove(key.VirtualKey);
        }

        // O estado das teclas acompanhado aqui vale também para entrada remota, que nem sempre
        // atualiza o estado assíncrono a tempo.
        var windows = _windowsDown || IsWindowsKeyDown();
        var control = _controlDown || User32.IsKeyDown(User32.VkControl);
        if (_interceptWinTab && windows && !control && key.VirtualKey == User32.VkTab)
        {
            Swallow(key, withWindowsKey: true);
            Post(() => OverviewKeyPressed?.Invoke(this, EventArgs.Empty));
            return true;
        }

        if (_interceptDesktopSwitch && windows && control && key.VirtualKey is User32.VkLeft or User32.VkRight)
        {
            var direction = key.VirtualKey == User32.VkLeft ? -1 : 1;
            Swallow(key, withWindowsKey: true);
            Post(() => SwitchKeyPressed?.Invoke(this, direction));
            return true;
        }

        if (_captureOverviewKeys && !windows && !control && !_altDown && !User32.IsKeyDown(User32.VkMenu))
        {
            OverviewKey? navigation = key.VirtualKey switch
            {
                User32.VkEscape => OverviewKey.Escape,
                User32.VkLeft => OverviewKey.Left,
                User32.VkRight => OverviewKey.Right,
                User32.VkReturn => OverviewKey.Enter,
                _ => null,
            };

            if (navigation is { } pressed)
            {
                Swallow(key, withWindowsKey: false);
                Post(() => OverviewNavigation?.Invoke(this, pressed));
                return true;
            }
        }

        if (_watchEscape && key.VirtualKey == User32.VkEscape)
        {
            Post(() => EscapePressed?.Invoke(this, EventArgs.Empty));
        }

        return false;
    }

    private void TrackModifier(uint key, bool isDown)
    {
        switch (key)
        {
            case User32.VkLeftWin or User32.VkRightWin:
                _windowsDown = isDown;
                break;
            case User32.VkControl or VkLeftControl or VkRightControl:
                _controlDown = isDown;
                break;
            case User32.VkMenu or VkLeftAlt or VkRightAlt:
                _altDown = isDown;
                break;
        }
    }

    private void Swallow(User32.KeyboardHookData key, bool withWindowsKey)
    {
        _swallowedKeys.Add(key.VirtualKey);
        _swallowedWithWindowsKey |= withWindowsKey;
    }

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (int)wParam == User32.WmLButtonDown)
        {
            var data = Marshal.PtrToStructure<User32.MouseHookData>(lParam);
            if (FindCaptionButton(data.Point) is { } pressed)
            {
                Post(() => CaptionButtonPressed?.Invoke(this, pressed));
            }
        }

        return User32.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    /// <summary>Pergunta à janela sob o cursor o que há ali (WM_NCHITTEST), como o próprio Windows faz.</summary>
    private static (nint, CaptionButton)? FindCaptionButton(User32.Point point)
    {
        var window = User32.GetAncestor(User32.WindowFromPoint(point), User32.GaRoot);
        if (window == 0)
        {
            return null;
        }

        // As janelas da dock não têm botões de título, e perguntar a elas esperaria a thread da interface.
        User32.GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId)
        {
            return null;
        }

        var position = (nint)((point.Y << 16) | (point.X & 0xFFFF));
        if (User32.SendMessageTimeout(window, User32.WmNcHitTest, 0, position, User32.SmtoAbortIfHung, HitTestTimeout, out var hit) == 0)
        {
            return null;
        }

        return (int)hit switch
        {
            User32.HtClose => (window, CaptionButton.Close),
            User32.HtMinButton => (window, CaptionButton.Minimize),
            _ => null,
        };
    }

    private void Post(Action action) => _dispatcher.BeginInvoke(action);
}
