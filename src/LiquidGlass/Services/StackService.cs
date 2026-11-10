namespace LiquidGlass.Services;

/// <summary>Um item de uma pilha: arquivo ou subpasta.</summary>
public sealed record StackEntry(string Name, string Path, bool IsFolder, DateTime Modified);

/// <summary>
/// "Pilhas", como no macOS: uma pasta fixada na dock abre em leque com o que tem dentro, os mais
/// recentes primeiro.
/// </summary>
public static class StackService
{
    /// <summary>Quantos itens a pilha mostra; o resto fica a um clique, no Explorador.</summary>
    public const int MaxEntries = 24;

    public static bool IsFolder(string? path) =>
        !string.IsNullOrEmpty(path) && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path);

    /// <summary>Arquivos e subpastas visíveis, do mais recente ao mais antigo (ocultos e de sistema ficam de fora).</summary>
    public static List<StackEntry> Load(string folder, int max = MaxEntries)
    {
        try
        {
            return new DirectoryInfo(folder)
                .EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System })
                .Where(entry => !entry.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.LastWriteTime)
                .Take(max)
                .Select(entry => new StackEntry(DisplayName(entry), entry.FullName, entry is DirectoryInfo, entry.LastWriteTime))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    /// <summary>Atalhos aparecem sem o ".lnk", como no Explorador.</summary>
    private static string DisplayName(FileSystemInfo entry) =>
        entry is FileInfo { Extension: var extension } && extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(entry.Name)
            : entry.Name;
}
