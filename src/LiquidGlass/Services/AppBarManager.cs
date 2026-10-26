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
    private readonly int _callbackMessage;
    private bool _isRegistered;

    public AppBarManager(HwndSource source)
    {
        _source = source;
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
            Bounds = User32.GetPrimaryMonitorInfo().Monitor;
            return;
        }

        var data = Shell32.CreateAppBarData(_source.Handle);
        data.Edge = ToNativeEdge(edge);
        data.Rect = User32.GetPrimaryMonitorInfo().Monitor;
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

    private static bool IsDesktopInForeground()
    {
        var foreground = User32.GetForegroundWindow();
        return foreground == 0 || DesktopClasses.Contains(User32.GetWindowClassName(foreground));
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
                // A área de trabalho às vezes é anunciada como "tela cheia"; ignoramos esse caso.
                var isFullScreen = lParam != 0 && !IsDesktopInForeground();
                FullScreenChanged?.Invoke(this, isFullScreen);
                break;
        }

        return 0;
    }
}
