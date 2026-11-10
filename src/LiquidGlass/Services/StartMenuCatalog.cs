using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using LiquidGlass.Resources;

namespace LiquidGlass.Services;

/// <summary>
/// O que o menu Iniciar de vidro mostra: apps instalados (a mesma pasta "Todos os apps" do
/// Windows), os fixados do menu Iniciar do Windows (importados uma vez), arquivos recentes,
/// páginas das Configurações e arquivos do índice de pesquisa do Windows.
/// </summary>
internal static class StartMenuCatalog
{
    public const string AppsFolder = @"shell:AppsFolder\";

    /// <summary>Ícone das páginas das Configurações: o do próprio app.</summary>
    public const string SettingsIcon = AppsFolder + "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

    private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;

    private static readonly string RecentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    private static readonly HashSet<string> NoiseFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "PackageCache", "AppData", "$Recycle.Bin", "__pycache__", "site-packages", "obj",
    };

    /// <summary>Quantos atalhos recentes olhar, no máximo, até achar os arquivos que ainda existem.</summary>
    private const int RecentScanLimit = 400;

    /// <summary>
    /// Páginas das Configurações que a pesquisa encontra. O nome e as palavras que levam a cada uma
    /// ficam nos textos da interface (SettingsPage_* e SettingsPageKeywords_*), em cada idioma.
    /// </summary>
    private static readonly SettingsPage[] SettingsPages =
    [
        new("Settings", "ms-settings:"),
        new("WiFi", "ms-settings:network-wifi"),
        new("NetworkStatus", "ms-settings:network-status"),
        new("Bluetooth", "ms-settings:bluetooth"),
        new("Display", "ms-settings:display"),
        new("Sound", "ms-settings:sound"),
        new("Notifications", "ms-settings:notifications"),
        new("Power", "ms-settings:powersleep"),
        new("Storage", "ms-settings:storagesense"),
        new("Background", "ms-settings:personalization-background"),
        new("Colors", "ms-settings:colors"),
        new("Themes", "ms-settings:themes"),
        new("LockScreen", "ms-settings:lockscreen"),
        new("InstalledApps", "ms-settings:appsfeatures"),
        new("DefaultApps", "ms-settings:defaultapps"),
        new("StartupApps", "ms-settings:startupapps"),
        new("YourInfo", "ms-settings:yourinfo"),
        new("SignInOptions", "ms-settings:signinoptions"),
        new("DateTime", "ms-settings:dateandtime"),
        new("Region", "ms-settings:regionlanguage"),
        new("Typing", "ms-settings:typing"),
        new("Mouse", "ms-settings:mousetouchpad"),
        new("Printers", "ms-settings:printers"),
        new("WindowsUpdate", "ms-settings:windowsupdate"),
        new("Privacy", "ms-settings:privacy"),
        new("WindowsSecurity", "windowsdefender:"),
        new("About", "ms-settings:about"),
        new("Vpn", "ms-settings:network-vpn"),
        new("Proxy", "ms-settings:network-proxy"),
        new("AirplaneMode", "ms-settings:network-airplanemode"),
        new("Clipboard", "ms-settings:clipboard"),
        new("Multitasking", "ms-settings:multitasking"),
        new("Gaming", "ms-settings:gaming-gamebar"),
        new("Accessibility", "ms-settings:easeofaccess"),
        new("ControlPanel", "control.exe"),
    ];

    /// <summary>Todos os apps instalados, como na lista "Todos os apps" do Windows. Chame numa thread STA.</summary>
    public static List<StartEntry> LoadApps()
    {
        var apps = new List<StartEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Shell32.EnumerateItems(AppsFolder, Shell32.SigdnParentRelativeParsing, (name, id) =>
        {
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(id))
            {
                var target = AppsFolder + id;
                apps.Add(new StartEntry(name, target, StartEntryKind.App, target));
            }

            return true;
        });

        apps.Sort((a, b) => Compare.Compare(a.Name, b.Name, CompareOptions.IgnoreCase));
        return apps;
    }

    /// <summary>
    /// Os fixados do menu Iniciar do Windows, pelo Export-StartLayout (o único caminho suportado:
    /// o arquivo em que o Windows os guarda não é documentado). Chame numa thread de fundo.
    /// </summary>
    public static List<PinnedApp> ImportWindowsPins()
    {
        var path = Path.Combine(Path.GetTempPath(), $"liquidglass-start-{Environment.ProcessId}.json");
        try
        {
            var start = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"Export-StartLayout -Path '{path}'\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using (var process = Process.Start(start))
            {
                if (process is null || !process.WaitForExit(15_000))
                {
                    process?.Kill();
                    return [];
                }
            }

            return File.Exists(path) ? ParseStartLayout(File.ReadAllText(path)) : [];
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or JsonException)
        {
            return [];
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Arquivo temporário: some na próxima limpeza do Windows.
            }
        }
    }

    /// <summary>
    /// Lê o que o Export-StartLayout grava. No Windows 11 atual, "pinnedList" é um texto com um
    /// cabeçalho e uma linha "idDoPacote,atalho" por app; em versões anteriores, uma lista de objetos.
    /// </summary>
    public static List<PinnedApp> ParseStartLayout(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("pinnedList", out var list))
        {
            return [];
        }

        var pins = new List<PinnedApp>();
        if (list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                AddPin(pins, Read(item, "packagedAppId"), Read(item, "desktopAppLink") ?? Read(item, "desktopAppId"));
            }
        }
        else if (list.ValueKind == JsonValueKind.String)
        {
            foreach (var line in (list.GetString() ?? string.Empty).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith('['))
                {
                    continue;
                }

                var comma = line.IndexOf(',', StringComparison.Ordinal);
                var packaged = comma < 0 ? line : line[..comma];
                var link = comma < 0 ? null : line[(comma + 1)..];
                AddPin(pins, packaged, link);
            }
        }

        return pins;

        static string? Read(JsonElement item, string name) =>
            item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>Os arquivos abertos mais recentemente (pastas não), do mais novo ao mais antigo. Chame numa thread STA.</summary>
    public static List<StartEntry> LoadRecent(int count, DateTime now)
    {
        var recent = new List<StartEntry>();
        if (!Directory.Exists(RecentFolder))
        {
            return recent;
        }

        // A pasta Recentes também guarda atalhos de protocolos (ms-gamingoverlay, ms-settings, sites),
        // às dezenas: os arquivos de verdade podem estar bem para baixo na lista.
        var shortcuts = new DirectoryInfo(RecentFolder).EnumerateFiles("*.lnk")
            .Where(file => !IsProtocolShortcut(file.Name))
            .OrderByDescending(file => file.LastWriteTime);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shortcut in shortcuts.Take(RecentScanLimit))
        {
            var target = Shell32.GetFileProperty(shortcut.FullName, Shell32.LinkTargetParsingPath);
            if (target is null || !File.Exists(target) || !seen.Add(target))
            {
                continue;
            }

            var name = Shell32.GetDisplayName(target) ?? Path.GetFileName(target);
            recent.Add(new StartEntry(name, target, StartEntryKind.File, target, RelativeTime(shortcut.LastWriteTime, now)));
            if (recent.Count == count)
            {
                break;
            }
        }

        return recent;
    }

    /// <summary>Atalho de protocolo ("ms-gamingoverlay--…", "https--…"), que não aponta para arquivo.</summary>
    public static bool IsProtocolShortcut(string fileName)
    {
        var dash = fileName.IndexOf("--", StringComparison.Ordinal);
        var prefix = dash > 0 ? fileName[..dash] : fileName;
        return fileName.StartsWith("ms-", StringComparison.OrdinalIgnoreCase)
            || prefix.Equals("http", StringComparison.OrdinalIgnoreCase)
            || prefix.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Inicial do grupo em "Todos os apps": a primeira letra sem acento e em maiúscula ("Área" fica
    /// em "A"), ou "#" para nomes que começam com número ou símbolo.
    /// </summary>
    public static string Initial(string? name)
    {
        var first = (name ?? string.Empty).TrimStart().Normalize(System.Text.NormalizationForm.FormD).FirstOrDefault();
        return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : "#";
    }

    /// <summary>Quando, em palavras, no idioma da interface: "Agora", "Há 5 min", "Há 20 h", "Ontem", "27 de out".</summary>
    public static string RelativeTime(DateTime when, DateTime now)
    {
        var elapsed = now - when;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return Strings.TimeNow;
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return Strings.TimeMinutesAgo((int)elapsed.TotalMinutes);
        }

        if (elapsed < TimeSpan.FromHours(24))
        {
            return Strings.TimeHoursAgo((int)elapsed.TotalHours);
        }

        if (when.Date == now.Date.AddDays(-1))
        {
            return Strings.TimeYesterday;
        }

        var format = when.Year == now.Year ? Strings.DateFormatThisYear : Strings.DateFormatOtherYear;
        return when.ToString(format, Strings.Culture).Replace(".", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Quão bem o nome responde à pesquisa, sem diferenciar acentos nem maiúsculas: 3 quando
    /// começa por ela, 2 quando uma palavra começa por ela, 1 quando só a contém, 0 quando não.
    /// </summary>
    public static int Score(string name, string query)
    {
        var score = ScoreExact(name, query);
        if (score > 0)
        {
            return score;
        }

        // Segunda chance sem pontuação nem espaços: "wifi" acha "Wi-Fi".
        return ScoreExact(Squash(name), Squash(query));
    }

    private static string Squash(string text) => string.Concat(text.Where(char.IsLetterOrDigit));

    private static int ScoreExact(string name, string query)
    {
        const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        if (name.Length == 0 || query.Length == 0)
        {
            return 0;
        }

        var index = Compare.IndexOf(name, query, Options);
        if (index < 0)
        {
            return 0;
        }

        if (index == 0)
        {
            return 3;
        }

        // Procura também começos de palavra depois da primeira ocorrência ("Code" em "Visual Studio Code").
        for (var at = index; at >= 0; at = Compare.IndexOf(name, query, at + 1, Options))
        {
            if (!char.IsLetterOrDigit(name[at - 1]))
            {
                return 2;
            }

            if (at + 1 >= name.Length)
            {
                break;
            }
        }

        return 1;
    }

    /// <summary>Os apps que respondem à pesquisa, dos mais parecidos aos menos.</summary>
    public static List<StartEntry> SearchApps(IEnumerable<StartEntry> apps, string query, int count) =>
        apps.Select(app => (app, score: Score(app.Name, query)))
            .Where(match => match.score > 0)
            .OrderByDescending(match => match.score)
            .ThenBy(match => match.app.Name.Length)
            .Take(count)
            .Select(match => match.app)
            .ToList();

    /// <summary>Páginas das Configurações pelo nome ou por palavras relacionadas.</summary>
    public static List<StartEntry> SearchSettings(string query, int count) =>
        SettingsPages.Select(page => (page, score: Math.Max(Score(page.Name, query) * 2, Score(page.Keywords, query))))
            .Where(match => match.score > 0)
            .OrderByDescending(match => match.score)
            .Take(count)
            .Select(match => new StartEntry(match.page.Name, match.page.Uri, StartEntryKind.Setting, SettingsIcon, Strings.SettingsApp))
            .ToList();

    /// <summary>Uma página das Configurações; o nome e as palavras-chave vêm no idioma da interface.</summary>
    private sealed record SettingsPage(string Id, string Uri)
    {
        public string Name => Strings.Lookup("SettingsPage_" + Id);

        public string Keywords => Strings.Lookup("SettingsPageKeywords_" + Id);
    }

    /// <summary>
    /// Arquivos da pasta do usuário pelo índice de pesquisa do Windows (o mesmo da pesquisa do
    /// Explorador). Para quando <paramref name="cancel"/> for acionado. Chame numa thread STA.
    /// </summary>
    public static List<StartEntry> SearchFiles(string query, int count, CancellationToken cancel)
    {
        var files = new List<(StartEntry Entry, int Score)>();
        if (Type.GetTypeFromProgID("ADODB.Connection") is not { } connectionType)
        {
            return [];
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');
        var sql = $"SELECT TOP {count * 12} System.ItemUrl, System.ItemNameDisplay FROM SystemIndex " +
            $"WHERE SCOPE='file:{EscapeSql(profile)}' AND System.ItemType <> 'Directory' " +
            $"AND System.ItemNameDisplay LIKE '%{EscapeLike(query)}%' ORDER BY System.DateModified DESC";

        dynamic? connection = null;
        try
        {
            connection = Activator.CreateInstance(connectionType)!;
            connection.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
            dynamic records = connection.Execute(sql);
            while (!(bool)records.EOF && !cancel.IsCancellationRequested)
            {
                var url = records.Fields.Item(0).Value as string;
                var name = records.Fields.Item(1).Value as string;
                var path = url is not null && url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? url[5..].Replace('/', '\\') : null;
                if (path is not null && name is not null && !IsNoise(path) && File.Exists(path))
                {
                    files.Add((new StartEntry(name, path, StartEntryKind.File, path, Path.GetDirectoryName(path)), Score(name, query)));
                }

                records.MoveNext();
            }

            records.Close();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Pesquisa do Windows desligada ou índice indisponível: sem arquivos.
        }
        finally
        {
            try
            {
                connection?.Close();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Já fechada.
            }
        }

        // Os mais parecidos primeiro; entre iguais, os modificados mais recentemente (ordem da consulta).
        return files.OrderByDescending(file => file.Score).Take(count).Select(file => file.Entry).ToList();
    }

    /// <summary>
    /// Arquivos que o índice guarda mas ninguém procura pelo Iniciar: caches de pacotes, pastas
    /// ocultas (".git", ".vscode"), dados de apps e os ".meta" que a Unity cria ao lado de tudo.
    /// </summary>
    public static bool IsNoise(string path)
    {
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var segment in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.StartsWith('.') || NoiseFolders.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    private static string EscapeSql(string text) => text.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeLike(string text) =>
        EscapeSql(text).Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal);

    /// <summary>Converte um app fixado em item do menu.</summary>
    public static StartEntry ToEntry(PinnedApp pin) => new(pin.Name, pin.Target, StartEntryKind.App, pin.Target);

    /// <summary>Converte um app da lista em fixado.</summary>
    public static PinnedApp ToPin(StartEntry entry)
    {
        var appId = entry.Target.StartsWith(AppsFolder, StringComparison.OrdinalIgnoreCase) ? entry.Target[AppsFolder.Length..] : null;
        return new PinnedApp(entry.Target, entry.Name, null, appId);
    }

    private static void AddPin(List<PinnedApp> pins, string? packagedAppId, string? desktopLink)
    {
        if (!string.IsNullOrWhiteSpace(packagedAppId))
        {
            var target = AppsFolder + packagedAppId;
            if (Shell32.GetDisplayName(target) is { } name)
            {
                pins.Add(new PinnedApp(target, name, null, packagedAppId));
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(desktopLink))
        {
            var link = Environment.ExpandEnvironmentVariables(desktopLink);
            if (File.Exists(link))
            {
                pins.Add(new PinnedApp(link, Shell32.GetDisplayName(link) ?? Path.GetFileNameWithoutExtension(link), null, null));
            }
        }
    }
}
