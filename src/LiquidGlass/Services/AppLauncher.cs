using System.ComponentModel;
using System.Diagnostics;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>Abre aplicativos e controla janelas de outros processos.</summary>
public static class AppLauncher
{
    public const string RecycleBin = "shell:RecycleBinFolder";
    public const string PowerSettings = "ms-settings:batterysaver";

    /// <summary>Lista de redes Wi-Fi do Windows 11 (o mesmo painel das Configurações rápidas).</summary>
    public const string WifiNetworks = "ms-availablenetworks:";
    public const string NetworkSettings = "ms-settings:network-status";

    public static bool Launch(string target)
    {
        try
        {
            var startInfo = target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("explorer.exe", target)
                : new ProcessStartInfo(target) { UseShellExecute = true };

            using var process = Process.Start(startInfo);
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Abre o menu Iniciar simulando a tecla Windows.</summary>
    public static void OpenStartMenu()
    {
        User32.keybd_event(User32.VkLeftWin, 0, 0, 0);
        User32.keybd_event(User32.VkLeftWin, 0, User32.KeyEventKeyUp, 0);
    }

    public static void Activate(nint hwnd)
    {
        if (User32.IsIconic(hwnd))
        {
            User32.ShowWindow(hwnd, User32.SwRestore);
        }

        if (!User32.SetForegroundWindow(hwnd))
        {
            User32.SwitchToThisWindow(hwnd, true);
        }
    }

    public static void Minimize(nint hwnd) => User32.ShowWindow(hwnd, User32.SwMinimize);

    public static void Close(nint hwnd) => User32.PostMessage(hwnd, User32.WmClose, 0, 0);
}
