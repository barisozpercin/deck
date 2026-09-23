using Deck.Shell.Media;

namespace Deck.Shell.Widgets;

internal sealed class NowPlayingWidget(WidgetContext context) : WidgetBase(context, "nowplaying")
{
    private NowPlaying Media => Context.Media.NowPlaying;

    public override void Start()
    {
        Context.Media.Refreshed += Push;
        Context.Media.Acquire();
    }

    public override void Stop()
    {
        Context.Media.Refreshed -= Push;
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

    public override void Push() => Post(new
    {
        hasSession = Media.HasSession,
        playing = Media.IsPlaying,
        title = Media.Title,
        artist = Media.Artist,
        app = Media.App
    });
}
