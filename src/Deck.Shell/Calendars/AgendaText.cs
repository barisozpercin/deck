using System.Globalization;

namespace Deck.Shell.Calendars;

internal enum AgendaState { Later, Soon, Now }

/// <summary>What the agenda says about an event, relative to "now". Pure, so every boundary is tested.</summary>
internal static class AgendaText
{
    /// <summary>Close enough to start that the tile should catch your eye.</summary>
    public static readonly TimeSpan SoonWindow = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan MinutesWindow = TimeSpan.FromMinutes(60);

    public static AgendaState State(DateTime start, DateTime end, DateTime now) =>
        now >= start && now < end ? AgendaState.Now
        : start > now && start - now <= SoonWindow ? AgendaState.Soon
        : AgendaState.Later;

    public static string When(DateTime start, DateTime end, DateTime now)
    {
        if (now >= start && now < end) return "now · until " + end.ToString("HH:mm", CultureInfo.InvariantCulture);

        var until = start - now;
        if (until <= MinutesWindow) return $"in {Math.Max(1, (int)Math.Ceiling(until.TotalMinutes))} min";

        string time = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        return (start.Date - now.Date).Days switch
        {
            0 => time,
            1 => "Tomorrow " + time,
            _ => start.ToString("ddd", CultureInfo.InvariantCulture) + " " + time
        };
    }

    /// <summary>Timed events that haven't ended, soonest first. All-day events belong on the month view.</summary>
    public static List<CalendarEntry> Upcoming(IEnumerable<CalendarEntry> entries, DateTime now) =>
        entries.Where(e => !e.AllDay && e.End > now).OrderBy(e => e.Start).ToList();
}
