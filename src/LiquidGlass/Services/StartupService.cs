using Microsoft.Win32;

namespace LiquidGlass.Services;

/// <summary>Liga ou desliga a inicialização automática com o Windows (chave Run do usuário).</summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LiquidGlassDock";

    /// <summary>O registro é a fonte da verdade: o instalador também liga e desliga esta opção.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled && Environment.ProcessPath is { } executable)
        {
            key.SetValue(ValueName, $"\"{executable}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
