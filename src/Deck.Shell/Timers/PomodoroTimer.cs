namespace Deck.Shell.Timers;

internal enum PomodoroPhase { Idle, Work, Break }

/// <summary>
/// Pomodoro cycles with one deliberate asymmetry: <b>the break starts on its own, the next work
/// block waits for a click.</b>
///
/// That puts the automation where willpower is weakest — actually stopping — and keeps a
/// deliberate decision where one is worth making. Auto-chaining everything drifts out of sync
/// with reality within a day; requiring a click for every phase means the breaks never happen.
/// </summary>
internal sealed class PomodoroTimer
{
    public static readonly TimeSpan WorkLength = TimeSpan.FromMinutes(25);
    public static readonly TimeSpan ShortBreak = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LongBreak = TimeSpan.FromMinutes(15);
    public const int LongBreakEvery = 4;

    private DateTime _endsAt;

    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Idle;
    public int CompletedBlocks { get; private set; }

    /// <summary>True between a break ending and the user starting the next block.</summary>
    public bool AwaitingNextBlock { get; private set; }

    /// <summary>Fires on phase changes only — the per-second display is driven by the host.</summary>
    public event Action? Changed;

    public event Action<string>? Alert;

    public TimeSpan Remaining
    {
        get
        {
            if (Phase == PomodoroPhase.Idle) return TimeSpan.Zero;
            var left = _endsAt - DateTime.Now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public void Toggle()
    {
        if (Phase == PomodoroPhase.Idle) StartWork();
        else Stop();
    }

    private void StartWork()
    {
        Phase = PomodoroPhase.Work;
        AwaitingNextBlock = false;
        // Wall-clock rather than accumulated ticks, so sleeping the machine doesn't leave a
        // block hanging with 12 minutes left three hours later.
        _endsAt = DateTime.Now + WorkLength;
        Changed?.Invoke();
    }

    public void Stop()
    {
        Phase = PomodoroPhase.Idle;
        AwaitingNextBlock = false;
        Changed?.Invoke();
    }

    public void ResetSession()
    {
        CompletedBlocks = 0;
        Stop();
    }

    /// <summary>Called once a second by the host.</summary>
    public void Tick()
    {
        if (Phase == PomodoroPhase.Idle) return;
        if (Remaining > TimeSpan.Zero) return;

        if (Phase == PomodoroPhase.Work)
        {
            CompletedBlocks++;
            bool isLong = CompletedBlocks % LongBreakEvery == 0;

            Phase = PomodoroPhase.Break;
            _endsAt = DateTime.Now + (isLong ? LongBreak : ShortBreak);
            Changed?.Invoke();

            Alert?.Invoke(isLong
                ? $"{CompletedBlocks} blocks done — long break started"
                : "Block done — break started");
        }
        else
        {
            Phase = PomodoroPhase.Idle;
            AwaitingNextBlock = true;
            Changed?.Invoke();

            Alert?.Invoke("Break over — press when you're ready for the next block");
        }
    }
}
