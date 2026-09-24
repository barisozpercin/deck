using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using IcalCalendar = Ical.Net.Calendar;

namespace Deck.Shell.Calendars;

/// <summary>
/// Turns iCal text into local-time entries for a window of time. Ical.Net does the hard parts:
/// repeats, skipped and moved instances, time zones.
/// </summary>
internal static class CalendarParser
{
    /// <summary>
    /// A stop for a malformed rule that would otherwise repeat forever. Expansion now covers a
    /// fixed horizon with its own early stop (see <see cref="Bounded"/>), so in the normal case
    /// this cap is never reached; it only guards a runaway feed.
    /// </summary>
    private const int MaxOccurrences = 20000;

    /// <summary>
    /// A rule that can never match (e.g. FREQ=HOURLY;BYMONTH=2;BYMONTHDAY=30 — February never has
    /// a 30th) would otherwise have Ical.Net search for a match without end. Capping the number of
    /// unmatched attempts makes it fail fast instead of freezing the deck.
    /// </summary>
    private static readonly EvaluationOptions Options = new() { MaxUnmatchedIncrementsLimit = 1000 };

    /// <summary>Throws on text that isn't iCal (a login page, an error page); the service turns that into the link's status.</summary>
    public static IcalCalendar Load(string ics) =>
        IcalCalendar.Load(ics) ?? throw new FormatException("not an iCal calendar");

    /// <summary>
    /// The whole pipeline for one refresh: load, drop pathological rules, then expand — all on a
    /// calendar instance nobody else has seen yet, so pruning and the never-matching-rule retry in
    /// <see cref="SafeOccurrences"/> are free to mutate it.
    /// </summary>
    public static List<CalendarEntry> Expand(string ics, DateTime fromLocal, DateTime toLocal)
    {
        var calendar = Load(ics);
        Prune(calendar);
        return Entries(calendar, fromLocal, toLocal);
    }

    /// <summary>
    /// Calendar apps don't create SECONDLY or MINUTELY repeats for real meetings; a feed that does
    /// (malformed, or a runaway export) would otherwise spend the occurrence cap on noise instead
    /// of real events, crowding them out. Dropping these before expansion keeps the cap meaningful.
    /// </summary>
    private static void Prune(IcalCalendar calendar)
    {
        foreach (var ev in calendar.Events.ToList())
        {
            if (ev.RecurrenceRule is { Frequency: Ical.Net.FrequencyType.Secondly or Ical.Net.FrequencyType.Minutely })
            {
                calendar.Events.Remove(ev);
            }
        }
    }

    public static List<CalendarEntry> Entries(IcalCalendar calendar, DateTime fromLocal, DateTime toLocal)
    {
        // A day early, so a meeting already in progress at fromLocal is still found.
        var start = new CalDateTime(fromLocal.AddDays(-1).ToUniversalTime(), CalDateTime.UtcTzId);

        // Occurrences come in start order. All-day ones are "floating" dates that sort by their
        // UTC reading, so allow a day's slack before stopping.
        DateTime stopUtc = toLocal.ToUniversalTime().AddDays(1);

        var entries = new List<CalendarEntry>();

        foreach (var occurrence in SafeOccurrences(calendar, start, stopUtc))
        {
            var period = occurrence.Period;
            if (occurrence.Source is not CalendarEvent ev) continue;

            bool allDay = ev.IsAllDay || !period.StartTime.HasTime;
            var endTime = period.EffectiveEndTime ?? period.StartTime;

            DateTime begin = allDay ? period.StartTime.Date.ToDateTime(TimeOnly.MinValue) : ToLocal(period.StartTime);
            DateTime end = allDay ? endTime.Date.ToDateTime(TimeOnly.MinValue) : ToLocal(endTime);
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

    /// <summary>
    /// RFC 5545: a DTSTART/DTEND with no Z and no TZID is local wall-clock time, not UTC. Ical.Net
    /// 5 reads it as UTC regardless, so a floating time has to bypass AsUtc entirely.
    /// </summary>
    private static DateTime ToLocal(CalDateTime time) =>
        time.IsFloating ? DateTime.SpecifyKind(time.Value, DateTimeKind.Local) : time.AsUtc.ToLocalTime();

    /// <summary>
    /// Ical.Net evaluates every event in a calendar together before yielding the first occurrence,
    /// so one event's rule that can never match throws before any other event's occurrences come
    /// out — not just its own. If that happens, find whichever event is responsible and retry
    /// without it, so a bad rule costs only its own event.
    /// </summary>
    private static List<Occurrence> SafeOccurrences(IcalCalendar calendar, CalDateTime start, DateTime stopUtc)
    {
        try
        {
            return Bounded(calendar.GetOccurrences<CalendarEvent>(start, Options), stopUtc);
        }
        catch (EvaluationException)
        {
            foreach (var ev in calendar.Events.ToList())
            {
                try
                {
                    Bounded(ev.GetOccurrences(start, Options), stopUtc);
                }
                catch (EvaluationException)
                {
                    calendar.Events.Remove(ev);
                }
            }

            try
            {
                return Bounded(calendar.GetOccurrences<CalendarEvent>(start, Options), stopUtc);
            }
            catch (EvaluationException)
            {
                return [];
            }
        }
    }

    /// <summary>Applies both the hard occurrence cap and the window's early stop while materialising.</summary>
    private static List<Occurrence> Bounded(IEnumerable<Occurrence> occurrences, DateTime stopUtc)
    {
        var list = new List<Occurrence>();
        foreach (var occurrence in occurrences.Take(MaxOccurrences))
        {
            if (occurrence.Period.StartTime.AsUtc > stopUtc) break;
            list.Add(occurrence);
        }

        return list;
    }
}
