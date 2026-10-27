using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LiquidGlass.Services;

public enum UpdateState
{
    /// <summary>Nenhuma procura feita ainda (ou a última foi automática e não achou nada).</summary>
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Installing,
    Failed,
}

/// <summary>
/// Procura versões novas nas releases do GitHub e instala direto da dock: baixa o instalador,
/// confere o SHA-256 que o GitHub publica para ele e o roda em modo silencioso. O instalador encerra
/// a dock (devolvendo a barra de tarefas), atualiza os arquivos e abre a versão nova.
/// </summary>
public sealed partial class UpdateService : ObservableObject, IDisposable
{
    private const string LatestRelease = "https://api.github.com/repos/PedroRuedas/Windows-Liquid-glass/releases/latest";
    private const string InstallerArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly DispatcherTimer _timer = new() { Interval = FirstCheckDelay };
    private CancellationTokenSource? _work;

    [ObservableProperty]
    private UpdateState _state;

    /// <summary>A versão nova encontrada, se houver.</summary>
    [ObservableProperty]
    private Release? _available;

    /// <summary>Andamento do download, de 0 a 1.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _message = "";

    public UpdateService()
    {
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await CheckAsync(userInitiated: false);
        };
    }

    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    public bool IsBusy => State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;

    /// <summary>Liga ou desliga a procura automática (a primeira, logo depois de a dock abrir).</summary>
    public void SetAutomatic(bool enabled)
    {
        _timer.Interval = FirstCheckDelay;
        _timer.IsEnabled = enabled;
    }

    /// <summary>
    /// Procura a release mais recente. A procura automática é silenciosa: sem rede, ela só tenta de
    /// novo mais tarde, sem mostrar erro.
    /// </summary>
    public async Task CheckAsync(bool userInitiated)
    {
        if (IsBusy)
        {
            return;
        }

        var previous = State;
        State = UpdateState.Checking;
        Message = "Procurando atualizações…";
        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(LatestRelease);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            var release = Release.Parse(await JsonDocument.ParseAsync(stream));

            if (release is not null && release.Version > CurrentVersion)
            {
                Available = release;
                State = UpdateState.Available;
                Message = string.Create(CultureInfo.CurrentCulture, $"A versão {release.Version} está disponível.");
            }
            else
            {
                Available = null;
                State = userInitiated ? UpdateState.UpToDate : UpdateState.Idle;
                Message = string.Create(CultureInfo.CurrentCulture, $"Você já tem a versão mais recente ({CurrentVersion}).");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            if (userInitiated)
            {
                Fail("Não foi possível procurar atualizações. Verifique a conexão e tente de novo.");
            }
            else
            {
                State = previous == UpdateState.Available ? previous : UpdateState.Idle;
            }
        }
    }

    /// <summary>Baixa a versão encontrada, confere a integridade e inicia a instalação.</summary>
    public async Task InstallAsync()
    {
        if (Available is not { } release || IsBusy)
        {
            return;
        }

        _work = new CancellationTokenSource();
        State = UpdateState.Downloading;
        Progress = 0;
        Message = string.Create(CultureInfo.CurrentCulture, $"Baixando a versão {release.Version}…");
        try
        {
            var path = await DownloadAsync(release, _work.Token);
            State = UpdateState.Installing;
            Message = "Instalando… a dock volta sozinha em alguns segundos.";

            // O instalador pede à dock que encerre antes de copiar os arquivos e a abre de novo no fim.
            Process.Start(new ProcessStartInfo(path, InstallerArguments) { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            State = UpdateState.Available;
            Message = string.Create(CultureInfo.CurrentCulture, $"A versão {release.Version} está disponível.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Fail(exception is InvalidDataException
                ? "O arquivo baixado não confere com o publicado. A atualização foi cancelada."
                : "Não foi possível baixar ou iniciar a atualização. Tente de novo mais tarde.");
        }
        finally
        {
            _work.Dispose();
            _work = null;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _work?.Cancel();
    }

    partial void OnStateChanged(UpdateState value) => OnPropertyChanged(nameof(IsBusy));

    private static Version ReadCurrentVersion()
    {
#if DEBUG
        // Para testar a atualização contra a release publicada: LIQUIDGLASS_UPDATE_FROM=0.3.0.
        if (System.Version.TryParse(Environment.GetEnvironmentVariable("LIQUIDGLASS_UPDATE_FROM"), out var pretended))
        {
            return pretended;
        }
#endif
        return Assembly.GetExecutingAssembly().GetName().Version is { } version
            ? new Version(version.Major, version.Minor, version.Build)
            : new Version(0, 0, 0);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"LiquidGlassDock/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Baixa para a pasta temporária e confere o SHA-256; um download incompleto ou alterado é apagado.</summary>
    private async Task<string> DownloadAsync(Release release, CancellationToken cancellation)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LiquidGlassUpdate");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, release.InstallerName);

        using var client = CreateClient();
        using var response = await client.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? release.InstallerSize;

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var target = File.Create(path))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    received += read;
                    Progress = total > 0 ? (double)received / total : 0;
                }
            }

            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("SHA-256 diferente do publicado.");
            }

            return path;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private void Fail(string message)
    {
        State = UpdateState.Failed;
        Message = message;
    }

    /// <summary>Uma release com instalador e o SHA-256 dele.</summary>
    public sealed partial record Release(Version Version, string InstallerName, string InstallerUrl, long InstallerSize, string Sha256, string Notes)
    {
        /// <summary>
        /// Lê a resposta da API do GitHub. Sem instalador ou sem SHA-256 (no campo "digest" do arquivo
        /// ou no texto da release), a release é ignorada: nada é instalado sem conferência.
        /// </summary>
        public static Release? Parse(JsonDocument document)
        {
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!System.Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            {
                return null;
            }

            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("LiquidGlassDock-Setup-", StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
                var sha256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest["sha256:".Length..]
                    : Sha256InNotes().Match(notes) is { Success: true } match ? match.Groups[1].Value : null;

                if (sha256 is not { Length: 64 })
                {
                    return null;
                }

                return new Release(
                    version,
                    name,
                    asset.GetProperty("browser_download_url").GetString() ?? "",
                    asset.GetProperty("size").GetInt64(),
                    sha256,
                    notes);
            }

            return null;
        }

        [GeneratedRegex(@"SHA-256\W[^`]*`([0-9a-fA-F]{64})`")]
        private static partial Regex Sha256InNotes();
    }
}
