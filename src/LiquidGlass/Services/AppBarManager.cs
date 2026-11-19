using System.Windows.Interop;
using LiquidGlass.Interop;
using LiquidGlass.Models;

namespace LiquidGlass.Services;

/// <summary>
/// Registra a dock como "app bar" do Windows: reserva espaço na borda da tela (janelas maximizadas
/// param antes da dock) e recebe o aviso de quando um app entra em tela cheia.
/// </summary>
internal sealed class AppBarManager : IDisposable
{
    private static readonly HashSet<string> DesktopClasses = ["Progman", "WorkerW"];

    private readonly HwndSource _source;
    private readonly Func<User32.MonitorInfo> _monitor;
    private readonly int _callbackMessage;
    private bool _isRegistered;

    public AppBarManager(HwndSource source, Func<User32.MonitorInfo> monitor)
    {
        _source = source;
        _monitor = monitor;
        _callbackMessage = User32.RegisterWindowMessage("LiquidGlass.AppBar");

        var data = Shell32.CreateAppBarData(source.Handle);
        data.CallbackMessage = (uint)_callbackMessage;
        _isRegistered = Shell32.SHAppBarMessage(Shell32.AbmNew, ref data) != 0;
        source.AddHook(WndProc);
    }

    /// <summary>Outra barra (como a nativa) mudou de lugar; a reserva precisa ser refeita.</summary>
    public event EventHandler? PositionChanged;

    /// <summary>Disparado quando um app entra (true) ou sai (false) de tela cheia.</summary>
    public event EventHandler<bool>? FullScreenChanged;

    /// <summary>Área da borda, em pixels físicos, que sobra depois das outras app bars (como a barra nativa).</summary>
    public User32.Rect Bounds { get; private set; }

    /// <summary>Reserva <paramref name="thickness"/> pixels físicos na borda indicada (0 para não reservar).</summary>
    public void Reserve(DockEdge edge, int thickness)
    {
        thickness = Math.Max(thickness, 0);
        if (!_isRegistered)
        {
            Bounds = _monitor().Monitor;
            return;
        }

        var data = Shell32.CreateAppBarData(_source.Handle);
        data.Edge = ToNativeEdge(edge);
        data.Rect = _monitor().Monitor;
        Shell32.SHAppBarMessage(Shell32.AbmQueryPos, ref data);

        // QUERYPOS devolve o retângulo já descontando outras barras; recorta só a nossa espessura.
        var available = data.Rect;
        switch (edge)
        {
            case DockEdge.Left:
                data.Rect.Right = data.Rect.Left + thickness;
                break;
            case DockEdge.Right:
                data.Rect.Left = data.Rect.Right - thickness;
                break;
            default:
                data.Rect.Top = data.Rect.Bottom - thickness;
                break;
        }

        Shell32.SHAppBarMessage(Shell32.AbmSetPos, ref data);
        Bounds = available;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        if (_isRegistered)
        {
            var data = Shell32.CreateAppBarData(_source.Handle);
            Shell32.SHAppBarMessage(Shell32.AbmRemove, ref data);
            _isRegistered = false;
        }
    }

    private static uint ToNativeEdge(DockEdge edge) => edge switch
    {
        DockEdge.Left => Shell32.AbeLeft,
        DockEdge.Right => Shell32.AbeRight,
        _ => Shell32.AbeBottom,
    };

    public void RefreshFullScreen()
    {
        var isFullScreen = IsForegroundFullScreen();
        FullScreenChanged?.Invoke(this, isFullScreen);
    }

    private bool IsForegroundFullScreen()
    {
        var foreground = User32.GetForegroundWindow();
        if (foreground == 0 || User32.IsZoomed(foreground)
            || DesktopClasses.Contains(User32.GetWindowClassName(foreground)))
        {
            return false;
        }

        var monitor = _monitor().Monitor;
        var bounds = Dwmapi.GetVisibleBounds(foreground);
        if (bounds.HasValue)
        {
            return MatchesMonitor(bounds.Value, monitor);
        }

        return User32.GetWindowRect(foreground, out var fallback) && MatchesMonitor(fallback, monitor);
    }

    internal static bool MatchesMonitor(User32.Rect window, User32.Rect monitor)
    {
        const int Tolerance = 1;
        return monitor.Width > 0 && monitor.Height > 0
            && Math.Abs(window.Left - monitor.Left) <= Tolerance
            && Math.Abs(window.Top - monitor.Top) <= Tolerance
            && Math.Abs(window.Right - monitor.Right) <= Tolerance
            && Math.Abs(window.Bottom - monitor.Bottom) <= Tolerance;
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != _callbackMessage || _callbackMessage == 0)
        {
            return 0;
        }

        switch ((int)wParam)
        {
            case Shell32.AbnPosChanged:
                PositionChanged?.Invoke(this, EventArgs.Empty);
                break;
            case Shell32.AbnFullScreenApp:
                // lParam 0 avisa que a tela cheia acabou. Com lParam diferente de 0 o aviso também
                // chega para janelas maximizadas, então conferimos a janela em primeiro plano.
                if (lParam == 0)
                {
                    FullScreenChanged?.Invoke(this, false);
                }
                else
                {
                    RefreshFullScreen();
                }

                break;
        }

        return 0;
    }
}
