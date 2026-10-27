using System.Windows.Interop;
using System.Windows.Threading;
using LiquidGlass.Interop;
using LiquidGlass.Models;

namespace LiquidGlass.Services;

/// <summary>
/// Acompanha as janelas de aplicativos abertas, a janela em primeiro plano e os pedidos de atenção,
/// usando o mesmo gancho de shell que a barra de tarefas do Windows usa.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private const int ShellWindowCreated = 1;
    private const int ShellWindowDestroyed = 2;
    private const int ShellWindowActivated = 4;
    private const int ShellRedraw = 6;
    private const int ShellWindowReplaced = 13;
    private const int ShellHighBit = 0x8000;
    private const int ShellRudeAppActivated = ShellWindowActivated | ShellHighBit;
    private const int ShellFlash = ShellRedraw | ShellHighBit;

    private static readonly HashSet<string> IgnoredClasses =
    [
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
    ];

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly Dictionary<uint, string?> _processPaths = [];
    private readonly DispatcherTimer _refreshDebounce;
    private readonly DispatcherTimer _safetyRefresh;
    private HwndSource? _source;
    private int _shellHookMessage;

    public WindowTracker()
    {
        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _refreshDebounce.Tick += (_, _) => Refresh();

        // O gancho não avisa quando uma janela apenas se esconde; uma varredura lenta cobre esses casos.
        _safetyRefresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _safetyRefresh.Tick += (_, _) => Refresh();
    }

    public event EventHandler? WindowsChanged;

    public event EventHandler<nint>? ForegroundChanged;

    public event EventHandler<nint>? WindowFlashed;

    public IReadOnlyList<AppWindow> Windows { get; private set; } = [];

    public nint ForegroundWindow { get; private set; }

    public void Attach(HwndSource source)
    {
        _source = source;
        _shellHookMessage = User32.RegisterWindowMessage("SHELLHOOK");
        User32.RegisterShellHookWindow(source.Handle);
        source.AddHook(WndProc);

        ForegroundWindow = User32.GetForegroundWindow();
        Refresh();
        _safetyRefresh.Start();
    }

    public void Refresh()
    {
        _refreshDebounce.Stop();
        var windows = EnumerateAppWindows();
        if (!windows.SequenceEqual(Windows))
        {
            Windows = windows;
            WindowsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _safetyRefresh.Stop();
        _refreshDebounce.Stop();
        if (_source is not null)
        {
            User32.DeregisterShellHookWindow(_source.Handle);
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != _shellHookMessage || _shellHookMessage == 0)
        {
            return 0;
        }

        switch ((int)wParam)
        {
            case ShellWindowCreated:
            case ShellWindowDestroyed:
            case ShellWindowReplaced:
                ScheduleRefresh();
                break;
            case ShellWindowActivated:
            case ShellRudeAppActivated:
                ForegroundWindow = lParam;
                ScheduleRefresh();
                ForegroundChanged?.Invoke(this, lParam);
                break;
            case ShellFlash:
                WindowFlashed?.Invoke(this, lParam);
                break;
        }

        return 0;
    }

    private void ScheduleRefresh()
    {
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    /// <summary>
    /// Janelas de app de todas as áreas de trabalho, de cima para baixo na ordem Z. As das outras
    /// áreas estão "cloaked"; quem chama decide quais delas são de fato de outra área.
    /// </summary>
    public List<AppWindow> EnumerateAllDesktops() => EnumerateAppWindows(includeCloaked: true);

    private List<AppWindow> EnumerateAppWindows(bool includeCloaked = false)
    {
        var windows = new List<AppWindow>();
        User32.EnumWindows(
            (hwnd, _) =>
            {
                if (IsAppWindow(hwnd, includeCloaked))
                {
                    windows.Add(Describe(hwnd));
                }

                return true;
            },
            0);

        // Ids de processo são reutilizados pelo sistema: descarta caminhos de processos que já fecharam.
        var alive = windows.Select(window => window.ProcessId).ToHashSet();
        foreach (var processId in _processPaths.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _processPaths.Remove(processId);
        }

        return windows;
    }

    private bool IsAppWindow(nint hwnd, bool includeCloaked)
    {
        if (!User32.IsWindowVisible(hwnd) || (!includeCloaked && Dwmapi.IsCloaked(hwnd)))
        {
            return false;
        }

        var exStyle = User32.GetExtendedStyle(hwnd);
        var isAppWindow = (exStyle & User32.WsExAppWindow) != 0;
        if (!isAppWindow)
        {
            if (User32.GetWindow(hwnd, User32.GwOwner) != 0)
            {
                return false;
            }

            if ((exStyle & (User32.WsExToolWindow | User32.WsExNoActivate)) != 0)
            {
                return false;
            }
        }

        User32.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == _ownProcessId)
        {
            return false;
        }

        return User32.GetWindowTextLength(hwnd) > 0 && !IgnoredClasses.Contains(User32.GetWindowClassName(hwnd));
    }

    private AppWindow Describe(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out var processId);
        if (!_processPaths.TryGetValue(processId, out var path))
        {
            path = Kernel32.GetProcessPath(processId);
            _processPaths[processId] = path;
        }

        var appId = Shell32.GetWindowProperty(hwnd, Shell32.AppUserModelId);
        return new AppWindow(hwnd, processId, User32.GetWindowTitle(hwnd), path, appId);
    }
}
