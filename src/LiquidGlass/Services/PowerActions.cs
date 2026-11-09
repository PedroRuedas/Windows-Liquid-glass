using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>
/// Bloquear, sair, suspender, hibernar, desligar e reiniciar, como no menu de energia do Windows.
/// Desligar e reiniciar não forçam o fechamento: apps com algo não salvo podem pedir para esperar,
/// e o Windows mostra a tela de sempre ("Estes apps estão impedindo o desligamento").
/// </summary>
internal static class PowerActions
{
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

    /// <summary>A opção "Hibernar" só aparece quando a hibernação está ligada no Windows.</summary>
    public static bool CanHibernate
    {
        get
        {
            try
            {
                return PowrProf.IsPwrHibernateAllowed();
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    public static bool Lock() => User32.LockWorkStation();

    public static bool SignOut() => User32.ExitWindowsEx(User32.EwxLogoff, 0);

    public static bool Sleep() => PowrProf.SetSuspendState(hibernate: false, force: false, disableWakeEvent: false);

    public static bool Hibernate() => PowrProf.SetSuspendState(hibernate: true, force: false, disableWakeEvent: false);

    /// <summary>Com a inicialização rápida ligada, desliga como o Windows faz: pronto para ligar mais rápido.</summary>
    public static bool Shutdown() => RunShutdown(FastStartupEnabled() ? "/s /hybrid /t 0" : "/s /t 0");

    public static bool Restart() => RunShutdown("/r /t 0");

    /// <summary>Argumentos do shutdown.exe; com /t 0 nada é forçado (um tempo maior implicaria /f).</summary>
    private static bool RunShutdown(string arguments)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
            using var process = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false, CreateNoWindow = true });
            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>A inicialização rápida depende da hibernação; sem ela, /hybrid falharia.</summary>
    private static bool FastStartupEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PowerKey);
            return key?.GetValue("HiberbootEnabled") is int enabled && enabled != 0 && CanHibernate;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Nome de exibição da conta (o da conta Microsoft, quando houver); senão, o nome de usuário.</summary>
    public static string UserDisplayName()
    {
        var size = 256;
        var buffer = new char[size];
        try
        {
            if (GetUserNameEx(NameDisplay, buffer, ref size) && size > 0)
            {
                return new string(buffer, 0, size);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return Environment.UserName;
    }

    private const int NameDisplay = 3;

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserNameEx(int format, [Out] char[] buffer, ref int size);
}
