using Deck.Shell.Layout;

namespace Deck.Shell.Widgets;

internal static class WidgetFactory
{
    public static IWidget? Create(WidgetPlacement placement, WidgetContext context) => placement switch
    {
        { Kind: "claude" } => new ClaudeWidget(context),
        { Kind: "weather" } => new WeatherWidget(context),
        { Kind: "nowplaying" } => new NowPlayingWidget(context),
        { Kind: "system" } => new SystemWidget(context),
        { Kind: "noise" } => new NoiseWidget(context),
        { Kind: "mic" } => new MicWidget(context),
        { Kind: "camera" } => new CameraWidget(context),
        { Kind: "clock" } => new ClockWidget(context),
        { Kind: "mixer" } => new MixerWidget(context),
        { Kind: "pomodoro" } => new PomodoroWidget(context),
        { Kind: "stopwatch" } => new StopwatchWidget(context),
        { Kind: "preset", Ref: { } id } => new PresetWidget(context, id),
        { Kind: "shortcut", Ref: { } id } => new ShortcutWidget(context, id),
        _ => null
    };
}
