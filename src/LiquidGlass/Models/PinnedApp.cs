namespace LiquidGlass.Models;

/// <summary>
/// Aplicativo fixado na dock.
/// </summary>
/// <param name="Target">O que é aberto ao clicar: atalho, executável ou <c>shell:AppsFolder\{AUMID}</c>.</param>
/// <param name="Name">Nome exibido no rótulo.</param>
/// <param name="ExecutablePath">Executável resolvido, usado para reconhecer janelas abertas.</param>
/// <param name="AppUserModelId">AUMID do app, usado para reconhecer janelas de apps da Store e PWAs.</param>
public sealed record PinnedApp(string Target, string Name, string? ExecutablePath, string? AppUserModelId);
