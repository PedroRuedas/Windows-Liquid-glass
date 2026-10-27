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

    /// <summary>Cabeçalho: versão do formato, número de grupos e um campo reservado (4 bytes cada).</summary>
    private const int HeaderVersionSize = 4;
    private const int HeaderReservedSize = 4;

    /// <summary>Grupo do Windows (recentes ou frequentes): só o número dele.</summary>
    private const int KnownCategoryIdSize = 4;

    /// <summary>Polinômio do CRC-64 que o Windows usa nos nomes dos arquivos das listas.</summary>
    private const ulong HashPolynomial = 0x92C64265D32139A4;

    private const int StreamSeekSet = 0;
    private const int StreamSeekCurrent = 1;

    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellItemClass = new("9AC9FBE1-E0A2-4AD6-B4EE-E212013EA917");
    private static readonly Guid ApplicationResolverClass = new("660B90C8-73A9-4B58-8CAE-355B7F55341B");

    private static readonly TimeSpan TemporaryLinkLifetime = TimeSpan.FromSeconds(30);

    private const string TemporaryLinkPattern = "jumplist-*.lnk";

    /// <summary>
    /// Quanto o menu espera pela lista antes de abrir sem ela. Ler um item do shell pode demorar
    /// (uma pasta recente num disco de rede desligado); a leitura continua e fica para a próxima vez.
    /// </summary>
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Recent\CustomDestinations");

    /// <summary>Listas já lidas, por arquivo; valem enquanto o arquivo não muda. Protegidas por <see cref="CacheLock"/>.</summary>
    private static readonly Dictionary<string, CachedList> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock CacheLock = new();

    /// <summary>Onde ficam, por alguns segundos, os atalhos que <see cref="Launch"/> abre.</summary>
    internal static string TemporaryFolder { get; } = Path.Combine(Path.GetTempPath(), "LiquidGlass");

    /// <summary>
    /// Um resolvedor por thread: um objeto COM só pode ser usado no apartamento em que nasceu, e a
    /// interface (não documentada) não tem proxy para ser chamada de outro.
    /// </summary>
    [ThreadStatic]
    private static IApplicationResolver? _resolver;

    [ThreadStatic]
    private static bool _resolverFailed;

    /// <summary>
    /// Lista de atalhos do app dono da janela ou do atalho fixado; vazia se ele não registrou nenhuma.
    /// Pode ser chamada de qualquer thread STA (a da interface, na dock).
    /// </summary>
    public static IReadOnlyList<JumpListCategory> Load(AppWindow? window, PinnedApp? pin)
    {
        if (ResolveAppId(window, pin) is not { } appId)
        {
            return [];
        }

        var path = Path.Combine(Folder, FileNameFor(appId));
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return [];
        }

        Task<IReadOnlyList<JumpListCategory>> reading;
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(path, out var cached) || cached.WriteTime != info.LastWriteTimeUtc)
            {
                cached = new CachedList(info.LastWriteTimeUtc, ReadInBackground(path));
                Cache[path] = cached;
            }

            reading = cached.Categories;
        }

        return reading.Wait(LoadTimeout) ? reading.Result : [];
    }

    /// <summary>
    /// Executa o item como a barra de tarefas faria: abrindo o atalho que o app registrou. Devolve
    /// <c>false</c> se não deu (o arquivo ou o programa do atalho não existe mais, por exemplo).
    /// </summary>
    public static bool Launch(JumpListEntry entry)
    {
        if (entry.ParsingName is { } parsingName)
        {
            return AppLauncher.Launch(parsingName);
        }

        if (entry.Link.IsEmpty)
        {
            return false;
        }

        // O atalho serializado tem exatamente o formato de um arquivo .lnk.
        DeleteStaleLinks();
        var file = Path.Combine(TemporaryFolder, $"jumplist-{Guid.NewGuid():N}.lnk");
        try
        {
            Directory.CreateDirectory(TemporaryFolder);
            File.WriteAllBytes(file, entry.Link.ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var launched = AppLauncher.Launch(file);
        DeleteLater(file);
        return launched;
    }

    /// <summary>
    /// Apaga os atalhos temporários que já serviram. O apagamento agendado não acontece se a dock
    /// fecha antes do prazo; eles ficam para a próxima abertura da dock ou de um item.
    /// </summary>
    public static void DeleteStaleLinks()
    {
        try
        {
            var folder = new DirectoryInfo(TemporaryFolder);
            if (!folder.Exists)
            {
                return;
            }

            foreach (var file in folder.EnumerateFiles(TemporaryLinkPattern).Where(file => DateTime.UtcNow - file.LastWriteTimeUtc > TemporaryLinkLifetime))
            {
                file.Delete();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Um atalho em uso agora fica para a próxima limpeza.
        }
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

    /// <summary>
    /// Lê a lista numa thread própria (STA, como o shell espera), para um item lento não travar a
    /// dock. Só dados gerenciados saem dela: os objetos COM nascem e morrem lá dentro.
    /// </summary>
    private static Task<IReadOnlyList<JumpListCategory>> ReadInBackground(string path)
    {
        var result = new TaskCompletionSource<IReadOnlyList<JumpListCategory>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                result.SetResult(Parse(File.ReadAllBytes(path)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result.SetResult([]);
            }
            catch (Exception exception)
            {
                // Erro inesperado: segue para a thread da interface, que o trata como antes.
                result.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Lista de atalhos",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    internal static List<JumpListCategory> Parse(byte[] data)
    {
        var categories = new List<JumpListCategory>();
        using var reader = ListReader.Open(data);
        if (reader is null)
        {
            return categories;
        }

        try
        {
            reader.Skip(HeaderVersionSize);
            var count = reader.ReadInt32();
            reader.Skip(HeaderReservedSize);
            for (var index = 0; index < count; index++)
            {
                if (ReadCategory(reader) is not { } category)
                {
                    break;
                }

                AddCategory(categories, category.Name, category.Entries, category.IsTasks);
            }
        }
        catch (Exception exception) when (exception is COMException or ArgumentException or InvalidCastException or IndexOutOfRangeException)
        {
            // Arquivo truncado ou de um formato novo: fica com o que já foi lido.
        }

        return categories;
    }

    /// <summary>
    /// Um grupo: o tipo, o nome (só nos personalizados), os itens e a marca de fim. <c>null</c>
    /// quando não dá para continuar: sem entender um item, não há como saber onde ele termina.
    /// </summary>
    private static (string? Name, List<JumpListEntry> Entries, bool IsTasks)? ReadCategory(ListReader reader)
    {
        var type = reader.ReadInt32();
        string? name = null;
        var entryCount = 0;
        switch (type)
        {
            case CustomCategory:
                name = reader.ReadName();
                entryCount = reader.ReadInt32();
                break;
            case KnownCategory:
                // Recentes e frequentes: os itens moram na lista automática, não aqui.
                reader.Skip(KnownCategoryIdSize);
                break;
            case TasksCategory:
                entryCount = reader.ReadInt32();
                break;
            default:
                return null;
        }

        var entries = new List<JumpListEntry>();
        for (var index = 0; index < entryCount; index++)
        {
            if (ReadEntry(reader) is not { } entry)
            {
                return null;
            }

            if (entry.Title.Length > 0 || entry.IsSeparator)
            {
                entries.Add(entry);
            }
        }

        return reader.ReadUInt32() == CategoryFooter ? (name, entries, type == TasksCategory) : null;
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

    private static JumpListEntry? ReadEntry(ListReader reader)
    {
        var classId = reader.ReadGuid();

        // Só as duas classes que o shell grava nas listas; nunca um objeto COM qualquer vindo de um arquivo.
        if (classId != ShellLinkClass && classId != ShellItemClass)
        {
            return null;
        }

        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(classId, throwOnError: true)!)!;
        try
        {
            var link = reader.ReadObject((IPersistStream)instance);
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

            return new JumpListEntry(ResolveTitle(Shell32.GetString(store, Shell32.Title)), link, null);
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

    /// <summary>
    /// Percorre o arquivo com uma posição só. Números e nomes saem direto dos bytes; os objetos COM
    /// são lidos pelo fluxo, porque só eles sabem onde terminam, e a posição continua de onde pararam.
    /// </summary>
    private sealed class ListReader(byte[] data, IStream stream) : IDisposable
    {
        private int _offset;

        public static ListReader? Open(byte[] data) => Shell32.CreateMemoryStream(data) is { } stream ? new ListReader(data, stream) : null;

        public int ReadInt32()
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(_offset));
            _offset += sizeof(int);
            return value;
        }

        public uint ReadUInt32() => unchecked((uint)ReadInt32());

        public Guid ReadGuid()
        {
            var value = new Guid(data.AsSpan(_offset, 16));
            _offset += 16;
            return value;
        }

        /// <summary>Nome de grupo: quantidade de caracteres (2 bytes) seguida do texto em UTF-16.</summary>
        public string ReadName()
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_offset)) * 2;
            var name = Encoding.Unicode.GetString(data, _offset + sizeof(ushort), length);
            _offset += sizeof(ushort) + length;
            return name;
        }

        public void Skip(int count) => _offset += count;

        /// <summary>Carrega o objeto a partir da posição atual e devolve os bytes que ele ocupou.</summary>
        public ReadOnlyMemory<byte> ReadObject(IPersistStream target)
        {
            var start = _offset;
            stream.Seek(start, StreamSeekSet, 0);
            target.Load(stream);
            _offset = (int)Position();
            return data.AsMemory(start, _offset - start);
        }

        public void Dispose() => Marshal.ReleaseComObject(stream);

        private long Position()
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
    }

    private sealed record CachedList(DateTime WriteTime, Task<IReadOnlyList<JumpListCategory>> Categories);
}
