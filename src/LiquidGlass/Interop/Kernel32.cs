using System.Runtime.InteropServices;

namespace LiquidGlass.Interop;

internal static class Kernel32
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessPowerThrottling = 4;
    private const uint PowerThrottlingCurrentVersion = 1;
    private const uint PowerThrottlingExecutionSpeed = 0x1;
    private const uint NormalPriorityClass = 0x20;
    private const uint BelowNormalPriorityClass = 0x4000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, [Out] char[] exeName, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(nint process, int informationClass, ref PowerThrottlingState information, int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPriorityClass(nint process, uint priorityClass);

    /// <summary>
    /// Liga ou desliga o "modo de eficiência" do Windows para este processo: EcoQoS (núcleos e
    /// frequências econômicos) e prioridade abaixo do normal. Desligado, o QoS volta a ser alto.
    /// </summary>
    public static void SetEfficiencyMode(bool enabled)
    {
        var state = new PowerThrottlingState
        {
            Version = PowerThrottlingCurrentVersion,
            ControlMask = PowerThrottlingExecutionSpeed,
            StateMask = enabled ? PowerThrottlingExecutionSpeed : 0,
        };

        var process = GetCurrentProcess();
        SetProcessInformation(process, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
        SetPriorityClass(process, enabled ? BelowNormalPriorityClass : NormalPriorityClass);
    }

    /// <summary>
    /// Caminho completo do executável de um processo, inclusive de processos elevados.
    /// </summary>
    public static string? GetProcessPath(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }
}
