using System.Runtime.InteropServices;

namespace LiquidGlass.Interop;

internal static class Dwmapi
{
    private const int DwmwaCloaked = 14;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    /// <summary>
    /// Janelas "cloaked" existem mas não são exibidas (apps UWP suspensos, outras áreas de trabalho virtuais).
    /// </summary>
    public static bool IsCloaked(nint hwnd)
    {
        return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
    }
}
