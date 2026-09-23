using Deck.Shell.Media;

namespace Deck.Shell.Widgets;

internal sealed class NowPlayingWidget(WidgetContext context) : WidgetBase(context, "nowplaying")
{
    /// <summary>The art the page already has. Art only travels when it changes — it's the one heavy field.</summary>
    private string? _artSent;

    private NowPlaying Media => Context.Media.NowPlaying;

    /// <summary>Only the 2×1 tile shows art; the 1×1 never needs it sent.</summary>
    private bool ShowsArt => Variant == "wide";

    public override void Start()
    {
        Context.Media.Refreshed += OnRefreshed;
        Context.Media.Acquire();
    }

    public override void Stop()
    {
        Context.Media.Refreshed -= OnRefreshed;
        Context.Media.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                _ = Media.TogglePlayPauseAsync();
                return true;

            case "next":
                _ = Media.SkipNextAsync();
                return true;

            case "prev":
                _ = Media.SkipPreviousAsync();
                return true;

            default:
                return false;
        }
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "nowplaying") return false;

        _ = Media.TogglePlayPauseAsync();
        return true;
    }

    public override void Push() => Send(includeArt: ShowsArt);

    private void OnRefreshed() => Send(includeArt: ShowsArt && Media.ArtDataUri != _artSent);

    private void Send(bool includeArt)
    {
        var data = new Dictionary<string, object?>
        {
            ["hasSession"] = Media.HasSession,
            ["playing"] = Media.IsPlaying,
            ["title"] = Media.Title,
            ["artist"] = Media.Artist,
            ["app"] = Media.App
        };

        if (includeArt)
        {
            data["art"] = Media.ArtDataUri;
            _artSent = Media.ArtDataUri;
        }

        Post(data);
    }
}
