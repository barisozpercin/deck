using Deck.Shell.Clock;

namespace Deck.Shell.Widgets;

internal sealed class ClockWidget(WidgetContext context) : WidgetBase(context, "clock")
{
    private string _lastShown = "";

    public override void Start()
    {
        Context.Tick.Ticked += OnTick;
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        Context.Tick.Ticked -= OnTick;
        Context.Tick.Release();
    }

    public override void Push() => Send(force: true);

    private void OnTick() => Send(force: false);

    private void Send(bool force)
    {
        var cities = WorldClock.Now();
        string shown = string.Join("|", cities.Select(c => $"{c.Time}{c.DayOffset}{c.IsLocal}"));

        // Ticks every second but the display only changes once a minute.
        if (!force && shown == _lastShown) return;
        _lastShown = shown;

        Post(new
        {
            cities = cities.Select(c => new
            {
                label = c.Label,
                time = c.Time,
                day = c.DayOffset,
                local = c.IsLocal
            })
        });
    }
}
