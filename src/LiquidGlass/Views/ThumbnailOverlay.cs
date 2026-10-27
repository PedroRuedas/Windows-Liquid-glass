using System.ComponentModel;
using System.Runtime.InteropServices;
using LiquidGlass.Interop;

namespace LiquidGlass.Views;

/// <summary>
/// Janela nativa invisível que cobre todos os monitores e só serve de "tela" para miniaturas do
/// DWM. Sem bitmap de redirecionamento ela não desenha nada (nem gasta memória com uma superfície):
/// o que aparece são só as miniaturas, compostas pelo próprio DWM. Fica escondida quando ociosa.
/// </summary>
internal sealed class ThumbnailOverlay : IDisposable
{
    private const string ClassName = "LiquidGlass.ThumbnailOverlay";

    // Mantido em campo estático: o Windows chama o procedimento enquanto a classe existir.
    private static readonly User32.WndProc Procedure = WindowProcedure;
    private static bool _isClassRegistered;

    private bool _isVisible;

    public ThumbnailOverlay()
    {
        RegisterClass();
        Bounds = User32.GetVirtualScreen();
        const uint ExStyle = (uint)(User32.WsExNoRedirectionBitmap | User32.WsExToolWindow | User32.WsExTopMost
            | User32.WsExTransparent | User32.WsExNoActivate);
        Handle = User32.CreateWindowEx(
            ExStyle, ClassName, string.Empty, (uint)User32.WsPopup, Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height,
            0, 0, Kernel32.GetModuleHandle(null), 0);

        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public nint Handle { get; }

    /// <summary>Área coberta, em pixels físicos da tela. As miniaturas são posicionadas relativas a ela.</summary>
    public User32.Rect Bounds { get; private set; }

    /// <summary>Converte um retângulo da tela para as coordenadas da janela.</summary>
    public User32.Rect ToClient(User32.Rect screen)
    {
        return new User32.Rect
        {
            Left = screen.Left - Bounds.Left,
            Top = screen.Top - Bounds.Top,
            Right = screen.Right - Bounds.Left,
            Bottom = screen.Bottom - Bounds.Top,
        };
    }

    public void Show()
    {
        // Monitores podem ter mudado desde a última vez.
        Bounds = User32.GetVirtualScreen();
        User32.SetWindowPos(
            Handle, User32.HwndTopMost, Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height,
            User32.SwpNoActivate | User32.SwpShowWindow);
        _isVisible = true;
    }

    public void Hide()
    {
        if (_isVisible)
        {
            User32.ShowWindow(Handle, User32.SwHide);
            _isVisible = false;
        }
    }

    public void Dispose() => User32.DestroyWindow(Handle);

    private static void RegisterClass()
    {
        if (_isClassRegistered)
        {
            return;
        }

        var windowClass = new User32.WindowClassEx
        {
            Size = Marshal.SizeOf<User32.WindowClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = Kernel32.GetModuleHandle(null),
            ClassName = ClassName,
        };

        _isClassRegistered = User32.RegisterClassEx(ref windowClass) != 0;
    }

    private static nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam)
    {
        // Cliques atravessam a janela enquanto uma animação passa por cima de outra janela.
        return message == User32.WmNcHitTest ? User32.HtTransparent : User32.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
