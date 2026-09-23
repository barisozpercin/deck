using System.Windows.Threading;

namespace Deck.Shell.Widgets;

/// <summary>The one-second heartbeat the timers, stats, clock and privacy poll share.</summary>
internal sealed class TickService : SharedService
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public event Action? Ticked;

    public TickService() => _timer.Tick += (_, _) => Ticked?.Invoke();

    protected override void OnStart() => _timer.Start();

    protected override void OnStop() => _timer.Stop();
}
