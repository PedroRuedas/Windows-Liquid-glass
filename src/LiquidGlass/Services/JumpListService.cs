using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using LiquidGlass.Interop;
using LiquidGlass.Models;

namespace LiquidGlass.Services;

/// <summary>
/// Lê as listas de atalhos que os apps registram para a barra de tarefas do Windows: tarefas como
/// "Nova guia" e "Nova janela anônima" e grupos como "Pastas recentes".
/// </summary>
/// <remarks>
/// O Windows guarda a lista de cada app em %APPDATA%\Microsoft\Windows\Recent\CustomDestinations,
/// num arquivo cujo nome é o CRC-64 do AppID em maiúsculas. Por dentro, cada grupo é uma sequência
/// de objetos COM serializados (atalhos .lnk ou itens do shell) que o próprio Windows sabe ler de
/// um fluxo. Recentes e frequentes automáticos ficam em outro formato e não entram aqui.
/// </remarks>
public static class JumpListService
{
    /// <summary>A barra do Windows mostra 10 por grupo; no menu da dock, menos já ocupa bastante.</summary>
    private const int MaxEntriesPerCategory = 6;

    private const int CustomCategory = 0;
    private const int KnownCategory = 1;
    private const int TasksCategory = 2;
    private const uint CategoryFooter = 0xBABFFBAB;

    /// <summary>Polinômio do CRC-64 que o Windows usa nos nomes dos arquivos das listas.</summary>
    private const ulong HashPolynomial = 0x92C64265D32139A4;

    private const int StreamSeekSet = 0;
    private const int StreamSeekCurrent = 1;

    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellItemClass = new("9AC9FBE1-E0A2-4AD6-B4EE-E212013EA917");
    private static readonly Guid ApplicationResolverClass = new("660B90C8-73A9-4B58-8CAE-355B7F55341B");

    private static readonly TimeSpan TemporaryLinkLifetime = TimeSpan.FromSeconds(30);

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Recent\CustomDestinations");

    private static IApplicationResolver? _resolver;
    private static bool _resolverFailed;

    /// <summary>Lista de atalhos do app dono da janela ou do atalho fixado; vazia se ele não registrou nenhuma.</summary>
    public static IReadOnlyList<JumpListCategory> Load(AppWindow? window, PinnedApp? pin)
    {
        if (ResolveAppId(window, pin) is not { } appId)
        {
            return [];
        }

        var path = Path.Combine(Folder, FileNameFor(appId));
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return Parse(data);
    }

    /// <summary>Executa o item como a barra de tarefas faria: abrindo o atalho que o app registrou.</summary>
    public static void Launch(JumpListEntry entry)
    {
        if (entry.ParsingName is { } parsingName)
        {
            AppLauncher.Launch(parsingName);
            return;
        }

        // O atalho serializado tem exatamente o formato de um arquivo .lnk.
        var folder = Path.Combine(Path.GetTempPath(), "LiquidGlass");
        var file = Path.Combine(folder, $"jumplist-{Guid.NewGuid():N}.lnk");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(file, entry.Link.ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        AppLauncher.Launch(file);
        DeleteLater(file);
    }

    /// <summary>Nome do arquivo da lista: CRC-64 do AppID em maiúsculas (UTF-16), em hexadecimal.</summary>
    internal static string FileNameFor(string appId)
    {
        var crc = ulong.MaxValue;
        foreach (var value in Encoding.Unicode.GetBytes(appId.ToUpperInvariant()))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ HashPolynomial : crc >> 1;
            }
        }

        return crc.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + ".customDestinations-ms";
    }

    /// <summary>
    /// O AppID que a barra de tarefas usa para o app: o resolvedor do shell o deduz mesmo quando o
    /// app não declara um (pelo executável) ou o declara só para o processo, como os apps Electron.
    /// </summary>
    private static string? ResolveAppId(AppWindow? window, PinnedApp? pin)
    {
        var resolver = Resolver();
        if (window is not null)
        {
            if (resolver?.GetAppIDForWindow(window.Handle, out var windowAppId, out _, out _, out _) == 0 && !string.IsNullOrEmpty(windowAppId))
            {
                return windowAppId;
            }

            if (window.AppUserModelId is { } declared)
            {
                return declared;
            }
        }

        if (pin?.AppUserModelId is { } pinned)
        {
            return pinned;
        }

        if (resolver is null || pin is null || Shell32.CreateShellItem(pin.Target) is not { } item)
        {
            return null;
        }

        try
        {
            return resolver.GetAppIDForShortcut(item, out var shortcutAppId) == 0 && !string.IsNullOrEmpty(shortcutAppId) ? shortcutAppId : null;
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    private static IApplicationResolver? Resolver()
    {
        if (_resolver is null && !_resolverFailed)
        {
            try
            {
                _resolver = Activator.CreateInstance(Type.GetTypeFromCLSID(ApplicationResolverClass, throwOnError: true)!) as IApplicationResolver;
            }
            catch (Exception exception) when (exception is COMException or InvalidCastException)
            {
                // Versões futuras do Windows podem mudar a interface; sem ela, vale o AUMID declarado.
            }

            _resolverFailed = _resolver is null;
        }

        return _resolver;
    }

    private static List<JumpListCategory> Parse(byte[] data)
    {
        var categories = new List<JumpListCategory>();
        var stream = Shell32.CreateMemoryStream(data);
        if (stream is null)
        {
            return categories;
        }

        try
        {
            var offset = 4; // versão
            var count = ReadInt32(data, ref offset);
            offset += 4;

            for (var index = 0; index < count; index++)
            {
                string? name = null;
                var entryCount = 0;
                var type = ReadInt32(data, ref offset);
                switch (type)
                {
                    case CustomCategory:
                        var length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
                        name = Encoding.Unicode.GetString(data, offset + 2, length * 2);
                        offset += 2 + (length * 2);
                        entryCount = ReadInt32(data, ref offset);
                        break;
                    case KnownCategory:
                        // Recentes e frequentes: os itens moram na lista automática, não aqui.
                        offset += 4;
                        break;
                    case TasksCategory:
                        entryCount = ReadInt32(data, ref offset);
                        break;
                    default:
                        return categories;
                }

                var entries = new List<JumpListEntry>();
                for (var entry = 0; entry < entryCount; entry++)
                {
                    // Sem saber ler o objeto, não há como saber onde ele termina: o resto do arquivo se perde.
                    if (ReadEntry(data, stream, ref offset) is not { } read)
                    {
                        return categories;
                    }

                    if (read.Title.Length > 0 || read.IsSeparator)
                    {
                        entries.Add(read);
                    }
                }

                if ((uint)ReadInt32(data, ref offset) != CategoryFooter)
                {
                    return categories;
                }

                AddCategory(categories, name, entries, isTasks: type == TasksCategory);
            }
        }
        catch (Exception exception) when (exception is COMException or ArgumentException or InvalidCastException or IndexOutOfRangeException)
        {
            // Arquivo truncado ou de um formato novo: fica com o que já foi lido.
        }
        finally
        {
            Marshal.ReleaseComObject(stream);
        }

        return categories;
    }

    private static void AddCategory(List<JumpListCategory> categories, string? name, List<JumpListEntry> entries, bool isTasks)
    {
        // Divisórias só fazem sentido entre itens.
        while (entries.Count > 0 && entries[^1].IsSeparator)
        {
            entries.RemoveAt(entries.Count - 1);
        }

        while (entries.Count > 0 && entries[0].IsSeparator)
        {
            entries.RemoveAt(0);
        }

        if (!isTasks)
        {
            entries = [.. entries.Where(entry => !entry.IsSeparator).Take(MaxEntriesPerCategory)];
        }

        if (entries.Count == 0)
        {
            return;
        }

        if (!isTasks && name is { Length: > 0 } && name[0] == '@')
        {
            name = Shell32.LoadIndirectString(name) ?? name;
        }

        categories.Add(new JumpListCategory(isTasks ? null : name ?? string.Empty, entries));
    }

    private static JumpListEntry? ReadEntry(byte[] data, IStream stream, ref int offset)
    {
        var classId = new Guid(data.AsSpan(offset, 16));
        offset += 16;

        // Só as duas classes que o shell grava nas listas; nunca um objeto COM qualquer vindo de um arquivo.
        if (classId != ShellLinkClass && classId != ShellItemClass)
        {
            return null;
        }

        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(classId, throwOnError: true)!)!;
        try
        {
            var start = offset;
            stream.Seek(start, StreamSeekSet, 0);
            ((IPersistStream)instance).Load(stream);
            offset = (int)Position(stream);

            if (classId == ShellItemClass)
            {
                var item = (IShellItem)instance;
                item.GetDisplayName(Shell32.SigdnNormalDisplay, out var displayName);
                item.GetDisplayName(Shell32.SigdnDesktopAbsoluteParsing, out var parsingName);
                return new JumpListEntry(displayName ?? string.Empty, ReadOnlyMemory<byte>.Empty, parsingName);
            }

            var store = (IPropertyStore)instance;
            if (Shell32.GetBoolean(store, Shell32.IsDestListSeparator))
            {
                return new JumpListEntry(string.Empty, ReadOnlyMemory<byte>.Empty, null, IsSeparator: true);
            }

            return new JumpListEntry(ResolveTitle(Shell32.GetString(store, Shell32.Title)), data.AsMemory(start, offset - start), null);
        }
        finally
        {
            Marshal.ReleaseComObject(instance);
        }
    }

    /// <summary>Apps empacotados gravam títulos indiretos ("@{pacote?ms-resource://...}"), resolvidos pelo shell.</summary>
    private static string ResolveTitle(string? title)
    {
        if (title is null)
        {
            return string.Empty;
        }

        if (title.StartsWith('@') || title.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            return Shell32.LoadIndirectString(title) ?? string.Empty;
        }

        return title;
    }

    private static long Position(IStream stream)
    {
        var position = Marshal.AllocCoTaskMem(sizeof(long));
        try
        {
            stream.Seek(0, StreamSeekCurrent, position);
            return Marshal.ReadInt64(position);
        }
        finally
        {
            Marshal.FreeCoTaskMem(position);
        }
    }

    private static int ReadInt32(byte[] data, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        offset += 4;
        return value;
    }

    private static async void DeleteLater(string file)
    {
        // O shell lê o atalho depois que a chamada volta; apagar na hora poderia cancelar a abertura.
        await Task.Delay(TemporaryLinkLifetime);
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Fica para a limpeza da pasta temporária do Windows.
        }
    }
}
