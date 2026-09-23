using System.Media;
using Deck.Shell.Timers;

namespace Deck.Shell.Widgets;

internal sealed class PomodoroWidget(WidgetContext context) : WidgetBase(context, "pomodoro")
{
    private readonly PomodoroTimer _timer = new();

    public override void Start()
    {
        _timer.Changed += Push;
        _timer.Alert += OnAlert;
        Context.Tick.Ticked += OnTick;
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        _timer.Changed -= Push;
        _timer.Alert -= OnAlert;
        Context.Tick.Ticked -= OnTick;
        Context.Tick.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                _timer.Toggle();
                return true;

            case "reset":
                _timer.ResetSession();
                return true;

            default:
                return false;
        }
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "pomodoro") return false;

        _timer.Toggle();
        return true;
    }

    public override void Push() => Post(new
    {
        phase = _timer.Phase.ToString().ToLowerInvariant(),
        remaining = TimeFormat.Countdown(_timer.Remaining),
        blocks = _timer.CompletedBlocks,
        awaiting = _timer.AwaitingNextBlock
    });

    private void OnTick()
    {
        _timer.Tick();
        if (_timer.Phase != PomodoroPhase.Idle) Push();
    }

    private void OnAlert(string message)
    {
        // Played directly rather than leaning on the notification's own sound, which Focus
        // Assist and fullscreen games can suppress. A timer you don't hear is not a timer.
        SystemSounds.Exclamation.Play();
        Context.Notifier.Show("Pomodoro", message);
        Push();
    }
}
