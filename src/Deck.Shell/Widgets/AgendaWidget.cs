using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Deck.Shell.Calendars;

namespace Deck.Shell.Widgets;

/// <summary>
/// The next meetings, and a one-click join. Focus starts on the soonest event; right-click steps
/// through the rest. The focused event is always the first one sent, so the 1×1 tile shows it
/// and the 2×1 tile lists it first.
/// </summary>
internal sealed class AgendaWidget(WidgetContext context) : WidgetBase(context, "agenda")
{
    private static readonly TimeSpan LookAhead = TimeSpan.FromDays(7);
    private const int Rows = 3;

    /// <summary>Re-read the calendar once a minute: often enough to drop an ended meeting, cheap enough not to matter.</summary>
    private const int ReloadTicks = 60;

    private List<CalendarEntry> _upcoming = [];

    /// <summary>
    /// The focused event by identity, not position: when an earlier meeting ends and drops off the
    /// list, an index would silently slide onto the next meeting — and a click would join it.
    /// Null means "the soonest".
    /// </summary>
    private string? _focusKey;

    private int _ticks;
    private string _lastShown = "";

    public override void Start()
    {
        Context.Calendar.Updated += OnUpdated;
        Context.Tick.Ticked += OnTick;
        Context.Calendar.Acquire();
        Context.Tick.Acquire();
        Reload();
    }

    public override void Stop()
    {
        Context.Calendar.Updated -= OnUpdated;
        Context.Tick.Ticked -= OnTick;
        Context.Calendar.Release();
        Context.Tick.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                Open();
                return true;

            case "next":
                var visible = Visible(DateTime.Now);
                if (visible.Count > 0) _focusKey = Key(visible[(FocusIndex(visible) + 1) % visible.Count]);
                Send(force: true);
                return true;

            default:
                return false;
        }
    }

    public override void Push() => Send(force: true);

    private void OnUpdated()
    {
        Reload();
        Send(force: true);
    }

    private void OnTick()
    {
        if (++_ticks % ReloadTicks == 0) Reload();
        Send(force: false);
    }

    private void Reload()
    {
        var now = DateTime.Now;
        var next = AgendaText.Upcoming(Context.Calendar.Entries(now, now + LookAhead), now);

        // A different list means the old focus points at the wrong meeting; start again at the soonest.
        if (!next.Select(Key).SequenceEqual(_upcoming.Select(Key))) _focusKey = null;
        _upcoming = next;
    }

    private static string Key(CalendarEntry e) => $"{e.Title}|{e.Start:O}";

    private List<CalendarEntry> Visible(DateTime now) => _upcoming.Where(e => e.End > now).ToList();

    /// <summary>Where the focused event sits now; back to the soonest if it has ended or vanished.</summary>
    private int FocusIndex(List<CalendarEntry> visible)
    {
        int index = _focusKey is null ? -1 : visible.FindIndex(e => Key(e) == _focusKey);
        if (index >= 0) return index;

        _focusKey = null;
        return 0;
    }

    private void Send(bool force)
    {
        var now = DateTime.Now;
        var visible = Visible(now);
        int focus = FocusIndex(visible);

        var rows = visible.Skip(focus).Concat(visible.Take(focus)).Take(Rows).Select(e => new
        {
            title = e.Title,
            when = AgendaText.When(e.Start, e.End, now),
            state = AgendaText.State(e.Start, e.End, now).ToString().ToLowerInvariant(),
            link = e.JoinUrl is not null
        }).ToArray();

        var data = new { status = Context.Calendar.Health, events = rows, total = visible.Count };

        // Ticks every second; "in 12 min" only changes once a minute.
        string shown = JsonSerializer.Serialize(data);
        if (!force && shown == _lastShown) return;
        _lastShown = shown;

        Post(data);
    }

    /// <summary>
    /// The meeting link if there is one, otherwise Google Calendar on that day. Opening a browser
    /// takes focus, and that's the point: you're joining the meeting.
    /// </summary>
    private void Open()
    {
        var visible = Visible(DateTime.Now);

        string url;
        if (visible.Count == 0)
        {
            url = "https://calendar.google.com/calendar/r";
        }
        else
        {
            var e = visible[FocusIndex(visible)];
            url = e.JoinUrl ?? $"https://calendar.google.com/calendar/r/day/{e.Start.Year}/{e.Start.Month}/{e.Start.Day}";
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Context.Notifier.Show("Couldn't open the meeting", ex.Message);
        }
    }
}
