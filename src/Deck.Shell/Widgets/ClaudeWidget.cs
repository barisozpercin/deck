using System.Windows.Threading;
using Deck.Shell.ClaudeStatus;
using Deck.Shell.Interop;

namespace Deck.Shell.Widgets;

/// <summary>What the Claude Code sessions are doing, and a jump to the one that wants you.</summary>
internal sealed class ClaudeWidget(WidgetContext context) : WidgetBase(context, "claude")
{
    private readonly ClaudeWatcher _watcher = new();
    private readonly HashSet<string> _previouslyWaiting = new(StringComparer.Ordinal);
    private DispatcherTimer? _timer;
    private int _focusIndex;
    private bool _polling;
    private bool _firstPoll = true;

    public override void Start()
    {
        // Two seconds: fast enough to notice a turn ending, slow enough that tailing several
        // multi-megabyte transcripts costs nothing worth measuring.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => _ = PollAsync();
        _timer.Start();

        _ = PollAsync();
    }

    public override void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                FocusNextSession();
                return true;

            case "notify-toggle":
                Context.Config.ClaudeNotifications = !Context.Config.ClaudeNotifications;
                Context.Config.Save();
                Push();
                return true;

            default:
                return false;
        }
    }

    public override void Push()
    {
        var sessions = _watcher.Sessions;
        var waiting = sessions.Where(s => !s.Working).ToArray();
        var working = sessions.Where(s => s.Working).ToArray();

        Post(new
        {
            waiting = waiting.Length,
            working = working.Length,
            // Waiting sessions are named first: that's the state that needs you to do something.
            names = string.Join(" · ", waiting.Concat(working).Select(s => s.Name)),
            notify = Context.Config.ClaudeNotifications
        });
    }

    private async Task PollAsync()
    {
        if (_polling || _timer is null) return;

        _polling = true;
        try
        {
            // Reads the disk — the first pass of the day walks today's whole transcripts, so
            // it must not run on the UI thread.
            await Task.Run(_watcher.Poll);

            // Taken off the deck while the poll was running.
            if (_timer is null) return;

            NotifyNewlyWaiting();
            Push();
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>
    /// The tile only helps if you look at it, and you're usually looking at another monitor —
    /// so a session becoming your problem is worth a notification.
    /// </summary>
    private void NotifyNewlyWaiting()
    {
        var waiting = _watcher.Sessions
            .Where(s => !s.Working)
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Don't announce everything that happens to be idle when the widget starts.
        if (!_firstPoll && Context.Config.ClaudeNotifications)
        {
            foreach (string name in waiting.Where(n => !_previouslyWaiting.Contains(n)))
                Context.Notifier.Show("Claude is waiting", $"{name} finished and wants your review.");
        }

        // The seen-set is updated even while muted, so unmuting doesn't dump a backlog of
        // notifications for sessions that went quiet an hour ago.
        _firstPoll = false;
        _previouslyWaiting.Clear();
        foreach (string name in waiting) _previouslyWaiting.Add(name);
    }

    /// <summary>Repeated presses cycle, so two waiting sessions are both reachable.</summary>
    private void FocusNextSession()
    {
        // false sorts before true, so sessions waiting on you come first.
        var ordered = _watcher.Sessions.OrderBy(s => s.Working).ToArray();
        if (ordered.Length == 0) return;

        var target = ordered[_focusIndex % ordered.Length];
        _focusIndex++;

        if (!WindowFocus.FocusProcessWindow(target.Pid))
            Context.Notifier.Show("Couldn't switch", $"No window found for {target.Name}.");
    }
}
