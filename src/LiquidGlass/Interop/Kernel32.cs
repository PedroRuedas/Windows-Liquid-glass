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

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadInformation(nint thread, int informationClass, ref PowerThrottlingState information, int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    /// <summary>
    /// Devolve ao Windows as páginas da memória do processo que não estão em uso. Elas voltam sob
    /// demanda (sem ler do disco, enquanto ainda estiverem na lista de espera) quando a dock é usada.
    /// </summary>
    public static void TrimWorkingSet() => SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);

    /// <summary>
    /// Tira a thread atual do EcoQoS. Os ganchos globais de teclado e mouse atrasariam o sistema
    /// inteiro se rodassem nos núcleos econômicos do modo de eficiência.
    /// </summary>
    public static void ExcludeCurrentThreadFromEfficiencyMode()
    {
        const int ThreadPowerThrottling = 3;
        var state = new PowerThrottlingState
        {
            Version = PowerThrottlingCurrentVersion,
            ControlMask = PowerThrottlingExecutionSpeed,
            StateMask = 0,
        };

        SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
    }

    /// <summary>Estado da bateria; <c>null</c> quando o Windows não informa.</summary>
    public static SystemPowerStatus? GetPowerStatus() => GetSystemPowerStatus(out var status) ? status : null;

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
    public struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }
}
