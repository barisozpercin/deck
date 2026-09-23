using Deck.Shell.Timers;

namespace Deck.Shell.Widgets;

internal sealed class StopwatchWidget(WidgetContext context) : WidgetBase(context, "stopwatch")
{
    private readonly StopwatchTimer _watch = new();

    public override void Start()
    {
        _watch.Changed += Push;
        Context.Tick.Ticked += OnTick;
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        _watch.Changed -= Push;
        Context.Tick.Ticked -= OnTick;
        Context.Tick.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                _watch.Toggle();
                return true;

            case "reset":
                _watch.Reset();
                return true;

            default:
                return false;
        }
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "stopwatch") return false;

        _watch.Toggle();
        return true;
    }

    public override void Push() => Post(new
    {
        running = _watch.IsRunning,
        elapsed = TimeFormat.Clock(_watch.Elapsed),
        hasElapsed = _watch.HasElapsed
    });

    private void OnTick()
    {
        if (_watch.IsRunning) Push();
    }
}
