using Deck.Shell.Countdowns;

namespace Deck.Shell.Widgets;

/// <summary>One saved countdown. Editing and deleting are the main window's job, since they change the config's list.</summary>
internal sealed class CountdownWidget(WidgetContext context, string id) : WidgetBase(context, "countdown", id)
{
    private string _lastShown = "";

    private Countdown? Countdown => Context.Config.Countdowns.FirstOrDefault(c => c.Id == Ref);

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
        if (Countdown is not { } countdown) return;

        var view = CountdownText.Describe(countdown.Target, countdown.HasTime, DateTime.Now);
        string date = CountdownText.DateLine(countdown.Target, countdown.HasTime);
        string shown = $"{countdown.Label}|{view.Value}|{view.Phase}|{date}";

        // Ticks every second, but the text changes at most once a minute.
        if (!force && shown == _lastShown) return;
        _lastShown = shown;

        Post(new
        {
            label = countdown.Label,
            value = view.Value,
            phase = view.Phase.ToString().ToLowerInvariant(),
            date
        });
    }
}
