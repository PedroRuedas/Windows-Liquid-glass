namespace LiquidGlass.Services;

internal static class AppDataPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LiquidGlass");

    public static string Settings => Path.Combine(Root, "settings.json");

    public static string TaskbarState => Path.Combine(Root, "taskbar-state.json");

    public static string CrashLog => Path.Combine(Root, "crash.log");

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}
