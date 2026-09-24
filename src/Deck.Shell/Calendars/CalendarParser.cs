using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using IcalCalendar = Ical.Net.Calendar;

namespace Deck.Shell.Calendars;

/// <summary>
/// Turns iCal text into local-time entries for a window of time. Ical.Net does the hard parts:
/// repeats, skipped and moved instances, time zones.
/// </summary>
internal static class CalendarParser
{
    /// <summary>A stop for a malformed rule that would otherwise repeat forever.</summary>
    private const int MaxOccurrences = 5000;

    /// <summary>Throws on text that isn't iCal (a login page, an error page); the service turns that into the link's status.</summary>
    public static IcalCalendar Load(string ics) =>
        IcalCalendar.Load(ics) ?? throw new FormatException("not an iCal calendar");

    public static List<CalendarEntry> Entries(IcalCalendar calendar, DateTime fromLocal, DateTime toLocal)
    {
        // A day early, so a meeting already in progress at fromLocal is still found.
        var start = new CalDateTime(fromLocal.AddDays(-1).ToUniversalTime(), CalDateTime.UtcTzId);

        // Occurrences come in start order. All-day ones are "floating" dates that sort by their
        // UTC reading, so allow a day's slack before stopping.
        DateTime stopUtc = toLocal.ToUniversalTime().AddDays(1);

        var entries = new List<CalendarEntry>();

        foreach (var occurrence in calendar.GetOccurrences<CalendarEvent>(start, null).Take(MaxOccurrences))
        {
            var period = occurrence.Period;
            if (period.StartTime.AsUtc > stopUtc) break;
            if (occurrence.Source is not CalendarEvent ev) continue;

            bool allDay = ev.IsAllDay || !period.StartTime.HasTime;
            var endTime = period.EffectiveEndTime ?? period.StartTime;

            DateTime begin = allDay ? period.StartTime.Date.ToDateTime(TimeOnly.MinValue) : period.StartTime.AsUtc.ToLocalTime();
            DateTime end = allDay ? endTime.Date.ToDateTime(TimeOnly.MinValue) : endTime.AsUtc.ToLocalTime();
            if (allDay && end <= begin) end = begin.AddDays(1);

            if (begin >= toLocal || end <= fromLocal) continue;

            string? conference = ev.Properties.FirstOrDefault(p => p.Name == "X-GOOGLE-CONFERENCE")?.Value?.ToString();

            entries.Add(new CalendarEntry(
                string.IsNullOrWhiteSpace(ev.Summary) ? "(no title)" : ev.Summary.Trim(),
                begin,
                end,
                allDay,
                MeetingLinks.Find(conference, ev.Location, ev.Description, ev.Url?.ToString())));
        }

        return entries.OrderBy(e => e.Start).ToList();
    }
}
