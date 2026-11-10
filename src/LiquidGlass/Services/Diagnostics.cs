using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using LiquidGlass.Resources;
using Microsoft.Win32;

namespace LiquidGlass.Services;

/// <summary>
/// "Copiar informações de diagnóstico": um texto para colar numa issue com a versão da dock, o
/// Windows, os monitores, as preferências que mudam o comportamento e os últimos erros. Caminhos
/// da pasta do usuário viram %USERPROFILE%, para não expor o nome da conta.
/// </summary>
public static class Diagnostics
{
    /// <summary>Quantas linhas do fim do crash.log entram no relatório.</summary>
    private const int CrashLines = 40;

    public static string Build(DockSettings settings)
    {
        var text = new StringBuilder();
        text.AppendLine("### Liquid Glass Dock");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Versão: {AppVersion()}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Windows: {WindowsVersion()}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Idioma: {Strings.Culture.Name} (Windows: {CultureInfo.InstalledUICulture.Name})");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Processo: {RuntimeInformation.ProcessArchitecture}, {RuntimeInformation.FrameworkDescription}");
        text.AppendLine();
        text.AppendLine("### Monitores");
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"- {bounds.Width}×{bounds.Height} em ({bounds.X}, {bounds.Y}), escala {ScaleOf(screen)}%{(screen.Primary ? ", principal" : string.Empty)}");
        }

        text.AppendLine();
        text.AppendLine("### Preferências");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"- Posição: {settings.Edge}, ícones {settings.IconSize:0} px, ampliação {(settings.MagnificationEnabled ? settings.Magnification.ToString("0.00", CultureInfo.InvariantCulture) : "desligada")}");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"- Barra do Windows: {settings.TaskbarMode}, ocultar a dock: {settings.AutoHide}, menu Iniciar de vidro: {settings.UseGlassStartMenu}");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"- Animações de janela: {settings.AnimateWindows}, cubo: {settings.AnimateWorkspaceSwitch}, Win+Tab: {settings.WinTabOpensOverview}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Apps fixados: {settings.PinnedApps.Count}");
        text.AppendLine();
        text.AppendLine("### Últimos erros (crash.log)");
        text.AppendLine("```");
        text.AppendLine(RecentErrors());
        text.AppendLine("```");
        return text.ToString();
    }

    /// <summary>Troca a pasta do usuário por %USERPROFILE% (também com barras normais, como em URIs).</summary>
    public static string Anonymize(string text, string userProfile)
    {
        if (string.IsNullOrEmpty(userProfile))
        {
            return text;
        }

        return text
            .Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            .Replace(userProfile.Replace('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O nome do produto no registro continua "Windows 10" no Windows 11: a partir do build 22000
    /// ele é corrigido, como faz o próprio winver.
    /// </summary>
    public static string DescribeWindows(string? productName, string? displayVersion, string? build, int revision)
    {
        var name = productName ?? "Windows";
        if (int.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 22000)
        {
            name = name.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        }

        var version = string.IsNullOrEmpty(displayVersion) ? string.Empty : " " + displayVersion;
        return string.Create(CultureInfo.InvariantCulture, $"{name}{version} (build {build}.{revision})");
    }

    private static string AppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "?";
    }

    private static string WindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return DescribeWindows(
            key?.GetValue("ProductName") as string,
            key?.GetValue("DisplayVersion") as string,
            key?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture),
            key?.GetValue("UBR") is int revision ? revision : 0);
    }

    private static int ScaleOf(System.Windows.Forms.Screen screen)
    {
        var center = new User32.Point { X = screen.Bounds.Left + (screen.Bounds.Width / 2), Y = screen.Bounds.Top + (screen.Bounds.Height / 2) };
        var monitor = User32.MonitorFromPoint(center, User32.MonitorDefaultToNearest);
        return Shcore.GetDpiForMonitor(monitor, Shcore.MonitorDpiEffective, out var dpi, out _) == 0 ? (int)Math.Round(dpi * 100 / 96.0) : 100;
    }

    private static string RecentErrors()
    {
        try
        {
            if (!File.Exists(AppDataPaths.CrashLog))
            {
                return "(nenhum)";
            }

            var lines = File.ReadLines(AppDataPaths.CrashLog).TakeLast(CrashLines);
            return Anonymize(string.Join(Environment.NewLine, lines), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        catch (IOException exception)
        {
            return "(não foi possível ler: " + exception.Message + ")";
        }
    }
}
