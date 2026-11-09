using System.Runtime.InteropServices;

namespace LiquidGlass.Interop;

internal static class PowrProf
{
    /// <summary>Suspende (<paramref name="hibernate"/> falso) ou hiberna o computador.</summary>
    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool force,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

    /// <summary>A hibernação está ligada (powercfg /hibernate on) e o hardware a suporta.</summary>
    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsPwrHibernateAllowed();
}
