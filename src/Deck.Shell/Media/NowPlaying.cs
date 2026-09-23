using Windows.Media.Control;

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

    private void Clear()
    {
        Title = "";
        Artist = "";
        App = "";
        IsPlaying = false;
        HasSession = false;
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
