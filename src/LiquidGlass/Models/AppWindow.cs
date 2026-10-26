namespace LiquidGlass.Models;

/// <summary>Janela de aplicativo que apareceria na barra de tarefas.</summary>
public sealed record AppWindow(nint Handle, uint ProcessId, string Title, string? ExecutablePath, string? AppUserModelId)
{
    /// <summary>Chave usada para agrupar janelas do mesmo app em um único ícone.</summary>
    public string GroupKey => AppUserModelId ?? ExecutablePath ?? $"hwnd:{Handle}";
}
