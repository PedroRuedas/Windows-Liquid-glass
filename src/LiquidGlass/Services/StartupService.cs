using Microsoft.Win32;

namespace LiquidGlass.Services;

/// <summary>Liga ou desliga a inicialização automática com o Windows (chave Run do usuário).</summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LiquidGlassDock";

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
