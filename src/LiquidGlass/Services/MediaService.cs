using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.Media.Control;

namespace LiquidGlass.Services;

/// <summary>O que um app está tocando, para o cartão de mídia nas miniaturas.</summary>
public sealed partial class MediaInfo : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _artist = string.Empty;

    [ObservableProperty]
    private BitmapSource? _artwork;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _canPrevious;

    [ObservableProperty]
    private bool _canNext;

    public required string SourceId { get; init; }

    public bool HasArtist => Artist.Length > 0;
}

/// <summary>
/// As sessões de mídia do Windows (as mesmas dos controles de volume do sistema): Spotify,
/// navegadores tocando um vídeo, players. Consultadas só quando o mouse para sobre um app aberto,
/// e não em segundo plano.
/// </summary>
public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Task? _refreshing;
    private List<MediaInfo> _sessions = [];

    /// <summary>O estado de um app mudou depois de um comando (tocar, pausar, pular).</summary>
    public event EventHandler? Changed;

    /// <summary>Atualiza a lista de sessões; chamadas seguidas enquanto uma está em andamento esperam a mesma.</summary>
    public Task RefreshAsync()
    {
        if (_refreshing is { IsCompleted: false } running)
        {
            return running;
        }

        _refreshing = LoadAsync();
        return _refreshing;
    }

    /// <summary>A sessão do app: pelo AppUserModelId ou pelo nome do executável das janelas dele.</summary>
    public MediaInfo? Find(IEnumerable<string?> appIds, IEnumerable<string?> executables) =>
        _sessions.FirstOrDefault(session => Matches(session.SourceId, appIds, executables));

    /// <summary>
    /// O Windows identifica a sessão pelo AppUserModelId do app ("Spotify.exe", "Chrome", "MSEdge"
    /// ou o de um app da Store): igual ao das janelas, ou ao nome do executável.
    /// </summary>
    public static bool Matches(string sourceId, IEnumerable<string?> appIds, IEnumerable<string?> executables)
    {
        if (string.IsNullOrEmpty(sourceId))
        {
            return false;
        }

        if (appIds.Any(id => string.Equals(id, sourceId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var source = Path.GetFileNameWithoutExtension(sourceId);
        return executables
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => Path.GetFileNameWithoutExtension(path!))
            .Any(name => string.Equals(name, source, StringComparison.OrdinalIgnoreCase)
                || (source.Length >= 4 && name.StartsWith(source, StringComparison.OrdinalIgnoreCase)));
    }

    public Task TogglePlayPauseAsync(MediaInfo info) => RunAsync(info, session => session.TryTogglePlayPauseAsync().AsTask());

    public Task NextAsync(MediaInfo info) => RunAsync(info, session => session.TrySkipNextAsync().AsTask());

    public Task PreviousAsync(MediaInfo info) => RunAsync(info, session => session.TrySkipPreviousAsync().AsTask());

    private async Task RunAsync(MediaInfo info, Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        if (_manager?.GetSessions().FirstOrDefault(session => session.SourceAppUserModelId == info.SourceId) is not { } session)
        {
            return;
        }

        try
        {
            await command(session);

            // O app leva um instante para avisar a música nova ou o estado.
            await Task.Delay(350);
            await UpdateAsync(info, session);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // O app fechou ou recusou o comando: nada a fazer.
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = new List<MediaInfo>();
            foreach (var session in _manager.GetSessions())
            {
                var info = new MediaInfo { SourceId = session.SourceAppUserModelId };
                await UpdateAsync(info, session);
                if (info.Title.Length > 0)
                {
                    sessions.Add(info);
                }
            }

            _sessions = sessions;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            _sessions = [];
        }
    }

    private static async Task UpdateAsync(MediaInfo info, GlobalSystemMediaTransportControlsSession session)
    {
        var playback = session.GetPlaybackInfo();
        info.IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        info.CanPrevious = playback.Controls.IsPreviousEnabled;
        info.CanNext = playback.Controls.IsNextEnabled;

        var properties = await session.TryGetMediaPropertiesAsync();
        if (properties is null)
        {
            return;
        }

        info.Title = properties.Title ?? string.Empty;
        info.Artist = properties.Artist ?? string.Empty;
        if (properties.Thumbnail is { } thumbnail)
        {
            info.Artwork = await LoadArtworkAsync(thumbnail);
        }
    }

    private static async Task<BitmapSource?> LoadArtworkAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var source = stream.AsStreamForRead();
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy);
            copy.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 128;
            image.StreamSource = copy;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return null;
        }
    }
}
