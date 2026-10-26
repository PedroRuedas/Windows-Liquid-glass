using System.Diagnostics;
using LiquidGlass.Interop;
using LiquidGlass.Models;

namespace LiquidGlass.Services;

/// <summary>Cria entradas de apps fixados a partir de atalhos, executáveis ou apps em execução.</summary>
public static class PinnedAppFactory
{
    private const string AppsFolderPrefix = @"shell:AppsFolder\";

    private static readonly string TaskbarPinsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    public static PinnedApp? FromPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = Shell32.GetFileProperty(path, Shell32.LinkTargetParsingPath);
            var executable = target is not null && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target : null;
            var appId = Shell32.GetFileProperty(path, Shell32.AppUserModelId);
            return new PinnedApp(path, Path.GetFileNameWithoutExtension(path), executable, appId);
        }

        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new PinnedApp(path, GetExecutableName(path), path, null);
        }

        return new PinnedApp(path, Shell32.GetDisplayName(path) ?? Path.GetFileName(path), null, null);
    }

    public static PinnedApp FromWindow(AppWindow window, string name)
    {
        if (window.AppUserModelId is { } appId && IsPackagedApp(window))
        {
            return new PinnedApp(AppsFolderPrefix + appId, name, null, appId);
        }

        var target = window.ExecutablePath ?? AppsFolderPrefix + window.AppUserModelId;
        return new PinnedApp(target, name, window.ExecutablePath, window.AppUserModelId);
    }

    /// <summary>Nome amigável de um app em execução (descrição do executável ou nome do pacote).</summary>
    public static string GetDisplayName(AppWindow window)
    {
        if (window.AppUserModelId is { } appId && IsPackagedApp(window)
            && Shell32.GetDisplayName(AppsFolderPrefix + appId) is { } packagedName)
        {
            return packagedName;
        }

        return window.ExecutablePath is { } path ? GetExecutableName(path) : window.Title;
    }

    /// <summary>Caminho usado para obter o ícone de um app em execução.</summary>
    public static string? GetIconSource(AppWindow window)
    {
        if (window.AppUserModelId is { } appId && IsPackagedApp(window))
        {
            return AppsFolderPrefix + appId;
        }

        return window.ExecutablePath;
    }

    /// <summary>Apps fixados na barra de tarefas do Windows, usados na primeira execução.</summary>
    public static IEnumerable<PinnedApp> ImportTaskbarPins()
    {
        if (!Directory.Exists(TaskbarPinsFolder))
        {
            yield break;
        }

        foreach (var shortcut in Directory.EnumerateFiles(TaskbarPinsFolder, "*.lnk").Order())
        {
            if (FromPath(shortcut) is { } pin)
            {
                yield return pin;
            }
        }
    }

    public static IEnumerable<PinnedApp> Defaults()
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        yield return new PinnedApp(explorer, "Explorador de Arquivos", explorer, null);

        const string settingsAppId = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";
        yield return new PinnedApp(AppsFolderPrefix + settingsAppId, "Configurações", null, settingsAppId);
    }

    /// <summary>
    /// Apps empacotados (Store/UWP) rodam dentro de hosts como ApplicationFrameHost; o executável
    /// não identifica o app, então usamos o AUMID.
    /// </summary>
    private static bool IsPackagedApp(AppWindow window)
    {
        var path = window.ExecutablePath;
        return path is null
            || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExecutableName(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (FileNotFoundException)
        {
            // O executável sumiu desde que a janela abriu; usa o nome do arquivo.
        }

        return Path.GetFileNameWithoutExtension(path);
    }
}
