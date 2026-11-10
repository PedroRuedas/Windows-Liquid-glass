using System.Runtime.InteropServices;

namespace LiquidGlass.Interop;

internal static class Shcore
{
    /// <summary>A escala que o usuário escolheu para o monitor (a de Configurações → Tela).</summary>
    public const int MonitorDpiEffective = 0;

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
