using Diagnostics = System.Diagnostics;

namespace Deck.Shell.Timers;

/// <summary>
/// Counts up. Answers "how long did that actually take", so it needs no alert and has none of
/// the reach problem the other tiles have.
/// </summary>
internal sealed class StopwatchTimer
{
    private readonly Diagnostics.Stopwatch _watch = new();

    public bool IsRunning => _watch.IsRunning;
    public TimeSpan Elapsed => _watch.Elapsed;
    public bool HasElapsed => _watch.Elapsed > TimeSpan.Zero;

    public event Action? Changed;

    public void Toggle()
    {
        if (_watch.IsRunning) _watch.Stop();
        else _watch.Start();

        Changed?.Invoke();
    }

    public void Reset()
    {
        _watch.Reset();
        Changed?.Invoke();
    }
}
