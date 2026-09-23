using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Deck.Shell.Media;

/// <summary>
/// Whatever Windows currently considers the active media session — Spotify, a browser tab, a
/// video player. Read through the same system integration the volume overlay uses, so it works
/// for any app without knowing anything about it.
/// </summary>
internal sealed class NowPlaying
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string App { get; private set; } = "";
    public bool IsPlaying { get; private set; }
    public bool HasSession { get; private set; }

    /// <summary>Album art as a data: URI, or null when the source doesn't publish any.</summary>
    public string? ArtDataUri { get; private set; }

    private string _artTrack = "";

    public async Task InitialiseAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch
        {
            // Media integration unavailable; the tile stays empty rather than breaking.
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            var session = _manager?.GetCurrentSession();
            if (session is null)
            {
                Clear();
                return;
            }

            var properties = await session.TryGetMediaPropertiesAsync();
            Title = properties.Title ?? "";
            Artist = properties.Artist ?? "";
            App = Apps.AppIdentityResolver.FriendlyAppName(session.SourceAppUserModelId)
                  ?? Shorten(session.SourceAppUserModelId);
            IsPlaying = session.GetPlaybackInfo().PlaybackStatus
                == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            HasSession = true;

            await RefreshArtAsync(properties.Thumbnail);
        }
        catch
        {
            Clear();
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        try
        {
            var session = _manager?.GetCurrentSession();
            if (session is not null) await session.TryTogglePlayPauseAsync();
        }
        catch { }
    }

    public async Task SkipNextAsync()
    {
        try
        {
            var session = _manager?.GetCurrentSession();
            if (session is not null) await session.TrySkipNextAsync();
        }
        catch { }
    }

    public async Task SkipPreviousAsync()
    {
        try
        {
            var session = _manager?.GetCurrentSession();
            if (session is not null) await session.TrySkipPreviousAsync();
        }
        catch { }
    }

    /// <summary>
    /// Read once per track rather than every poll, since the image is tens of kilobytes. A track
    /// with no art yet is retried: players often publish the title a moment before the art.
    /// </summary>
    private async Task RefreshArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        string track = $"{App}|{Title}|{Artist}";
        if (track == _artTrack) return;

        ArtDataUri = await ReadArtAsync(thumbnail);
        if (ArtDataUri is not null) _artTrack = track;
    }

    private static async Task<string?> ReadArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;

        try
        {
            using var stream = await thumbnail.OpenReadAsync();

            // Anything this large isn't cover art worth pushing through the page every track.
            if (stream.Size == 0 || stream.Size > 2_000_000) return null;

            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);

            string type = string.IsNullOrEmpty(stream.ContentType) ? "image/png" : stream.ContentType;
            return $"data:{type};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }

    private void Clear()
    {
        Title = "";
        Artist = "";
        App = "";
        IsPlaying = false;
        HasSession = false;
        ArtDataUri = null;
        _artTrack = "";
    }

    /// <summary>"Spotify.exe" or a long package id — neither is worth a tile's width.</summary>
    private static string Shorten(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return "";

        string name = appId.Split('!')[0];
        name = name.Split('\\')[^1];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        int dot = name.LastIndexOf('.');
        if (dot > 0 && dot < name.Length - 1) name = name[(dot + 1)..];

        return name;
    }
}
