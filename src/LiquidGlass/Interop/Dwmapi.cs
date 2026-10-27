using System.Runtime.InteropServices;

namespace LiquidGlass.Interop;

internal static class Dwmapi
{
    private const int DwmwaTransitionsForceDisabled = 3;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;

    private const int TnpRectDestination = 0x1;
    private const int TnpRectSource = 0x2;
    private const int TnpOpacity = 0x4;
    private const int TnpVisible = 0x8;
    private const int TnpSourceClientAreaOnly = 0x10;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out User32.Rect value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out User32.Point size);

    /// <summary>Tamanho da janela de origem, em pixels (de uma janela minimizada, o da última imagem).</summary>
    public static (int Width, int Height)? QuerySourceSize(nint thumbnail)
    {
        return DwmQueryThumbnailSourceSize(thumbnail, out var size) == 0 && size.X > 0 && size.Y > 0 ? (size.X, size.Y) : null;
    }

    /// <summary>
    /// Janelas "cloaked" existem mas não são exibidas (apps UWP suspensos, outras áreas de trabalho virtuais).
    /// </summary>
    public static bool IsCloaked(nint hwnd)
    {
        return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    /// <summary>
    /// Liga ou desliga as animações nativas da janela (abrir, fechar, minimizar). Funciona também em
    /// janelas de outros processos: a dock desliga a animação nativa enquanto toca a sua.
    /// </summary>
    public static void SetTransitionsDisabled(nint hwnd, bool disabled)
    {
        var value = disabled ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaTransitionsForceDisabled, ref value, sizeof(int));
    }

    /// <summary>Retângulo visível da janela, sem as bordas invisíveis de redimensionamento.</summary>
    public static User32.Rect? GetVisibleBounds(nint hwnd)
    {
        return DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out User32.Rect rect, Marshal.SizeOf<User32.Rect>()) == 0
            ? rect
            : null;
    }

    /// <summary>Miniatura viva de <paramref name="source"/> desenhada pelo DWM por cima de <paramref name="destination"/>.</summary>
    public static nint RegisterThumbnail(nint destination, nint source)
    {
        return DwmRegisterThumbnail(destination, source, out var thumbnail) == 0 ? thumbnail : 0;
    }

    public static void UnregisterThumbnail(nint thumbnail)
    {
        if (thumbnail != 0)
        {
            DwmUnregisterThumbnail(thumbnail);
        }
    }

    /// <summary>
    /// Posiciona a miniatura (em pixels físicos, relativos à janela de destino). Com
    /// <paramref name="source"/>, mostra só esse trecho da janela de origem.
    /// </summary>
    public static bool UpdateThumbnail(nint thumbnail, User32.Rect destination, byte opacity, bool visible, User32.Rect? source = null)
    {
        var properties = new ThumbnailProperties
        {
            Flags = TnpRectDestination | TnpOpacity | TnpVisible | TnpSourceClientAreaOnly,
            Destination = destination,
            Opacity = opacity,
            Visible = visible ? 1 : 0,
        };

        if (source is { } sourceRect)
        {
            properties.Flags |= TnpRectSource;
            properties.Source = sourceRect;
        }

        return DwmUpdateThumbnailProperties(thumbnail, ref properties) == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        public int Flags;
        public User32.Rect Destination;
        public User32.Rect Source;
        public byte Opacity;
        public int Visible;
        public int SourceClientAreaOnly;
    }
}
