using System.Windows.Threading;
using Deck.Shell.Weather;

namespace Deck.Shell.Widgets;

internal sealed class WeatherWidget(WidgetContext context) : WidgetBase(context, "weather")
{
    private readonly WeatherService _weather = new();
    private DispatcherTimer? _timer;

    public override void Start()
    {
        // Weather moves slowly and the service is free — 15 minutes is plenty and stays polite.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    public override void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _weather.Dispose();
    }

    public override void Push()
    {
        var reading = _weather.Latest;
        var (icon, label) = reading is null
            ? ("🌡️", "—")
            : WeatherService.Describe(reading.Code);

        Post(new
        {
            available = reading is not null,
            icon,
            label,
            temp = reading is null ? "–" : $"{Math.Round(reading.TempC)}°",
            high = reading is null ? "" : $"{Math.Round(reading.HighC)}°",
            low = reading is null ? "" : $"{Math.Round(reading.LowC)}°",
            feels = reading is null ? "" : $"{Math.Round(reading.FeelsC)}°",
            // Stale is surfaced rather than hidden: a cached number shown as current is the
            // same lying-tile problem as a mute button that didn't mute.
            stale = _weather.IsStale,
            error = _weather.Error
        });
    }

    private async Task RefreshAsync()
    {
        await _weather.RefreshAsync();
        if (_timer is not null) Push();
    }
}
