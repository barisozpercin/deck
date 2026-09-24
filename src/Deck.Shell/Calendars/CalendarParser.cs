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
    /// The last-resort stop for a runaway feed, once <see cref="Prune"/> and the per-event cap
    /// (<see cref="MaxOccurrencesPerEvent"/>) have already done their work — e.g. hundreds of
    /// distinct events each just under their own cap. In the normal case this is never reached.
    /// </summary>
    private const int MaxOccurrences = 200000;

    /// <summary>
    /// One event's share of <see cref="MaxOccurrences"/>. Without a per-event cap, a single event
    /// that slips past <see cref="Prune"/> — or simply repeats often enough for long enough — can
    /// fill the whole overall cap by itself and crowd out every other event sharing it.
    /// </summary>
    private const int MaxOccurrencesPerEvent = 2000;

    /// <summary>
    /// No real meeting series fires more than once every 30 minutes. <see cref="Prune"/> uses this
    /// to drop a rule that would, whether or not its FREQ alone looks pathological — e.g.
    /// FREQ=HOURLY;BYMINUTE=0,1,...,59 or FREQ=DAILY;BYHOUR=0..23;BYMINUTE=0..59 both pass a
    /// FREQ-only check but would still fill the occurrence caps with noise instead of real events.
    /// </summary>
    private const int MaxEstimatedOccurrencesPerDay = 48;

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
    /// <see cref="SafeOccurrences"/> are free to mutate it. <c>Truncated</c> says whether the
    /// per-event or overall occurrence cap dropped anything, so the service can say so in the
    /// link's status instead of truncating silently.
    /// </summary>
    public static (List<CalendarEntry> Entries, bool Truncated) Expand(string ics, DateTime fromLocal, DateTime toLocal)
    {
        var calendar = Load(ics);
        Prune(calendar);
        var entries = Entries(calendar, fromLocal, toLocal, out bool truncated);
        return (entries, truncated);
    }

    /// <summary>
    /// Calendar apps don't create SECONDLY or MINUTELY repeats for real meetings, and nothing
    /// legitimate fires more than <see cref="MaxEstimatedOccurrencesPerDay"/> times a day either —
    /// a feed that does (malformed, or a runaway export) would otherwise spend the occurrence caps
    /// on noise instead of real events, crowding them out. Dropping these before expansion keeps
    /// the caps meaningful.
    /// </summary>
    private static void Prune(IcalCalendar calendar)
    {
        foreach (var ev in calendar.Events.ToList())
        {
            if (ev.RecurrenceRule is { } rule && EstimatedOccurrencesPerDay(rule) > MaxEstimatedOccurrencesPerDay)
            {
                calendar.Events.Remove(ev);
            }
        }
    }

    /// <summary>
    /// A rough upper bound, not an exact count — enough to tell a plausible meeting series from a
    /// rule that fires far too often, without evaluating it. SECONDLY and MINUTELY are always over
    /// the limit. For HOURLY, the BYHOUR list (when present) says how many hours a day it can fire
    /// in, else all 24; for DAILY and coarser, the same list says how many hours a day, else just 1
    /// (the rule's own start time). Either way, BYMINUTE and BYSECOND (when present) multiply that
    /// further, since each is another firing within the hour.
    /// </summary>
    private static double EstimatedOccurrencesPerDay(RecurrenceRule rule)
    {
        if (rule.Frequency is Ical.Net.FrequencyType.Secondly or Ical.Net.FrequencyType.Minutely) return double.MaxValue;

        int hoursPerDay = rule.ByHour.Count > 0
            ? rule.ByHour.Count
            : rule.Frequency == Ical.Net.FrequencyType.Hourly ? 24 : 1;

        return (double)hoursPerDay * Math.Max(1, rule.ByMinute.Count) * Math.Max(1, rule.BySecond.Count);
    }

    public static List<CalendarEntry> Entries(IcalCalendar calendar, DateTime fromLocal, DateTime toLocal) =>
        Entries(calendar, fromLocal, toLocal, out _);

    /// <summary>As above, but also says whether the per-event or overall occurrence cap dropped anything.</summary>
    public static List<CalendarEntry> Entries(IcalCalendar calendar, DateTime fromLocal, DateTime toLocal, out bool truncated)
    {
        // A day early, so a meeting already in progress at fromLocal is still found.
        var start = new CalDateTime(fromLocal.AddDays(-1).ToUniversalTime(), CalDateTime.UtcTzId);

        // Occurrences come in start order. All-day ones are "floating" dates that sort by their
        // UTC reading, so allow a day's slack before stopping.
        DateTime stopUtc = toLocal.ToUniversalTime().AddDays(1);

        var entries = new List<CalendarEntry>();

        foreach (var occurrence in SafeOccurrences(calendar, start, stopUtc, out truncated))
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
    private static List<Occurrence> SafeOccurrences(IcalCalendar calendar, CalDateTime start, DateTime stopUtc, out bool truncated)
    {
        try
        {
            return Bounded(calendar.GetOccurrences<CalendarEvent>(start, Options), stopUtc, out truncated);
        }
        catch (EvaluationException)
        {
            foreach (var ev in calendar.Events.ToList())
            {
                try
                {
                    Bounded(ev.GetOccurrences(start, Options), stopUtc, out _);
                }
                catch (EvaluationException)
                {
                    calendar.Events.Remove(ev);
                }
            }

            try
            {
                return Bounded(calendar.GetOccurrences<CalendarEvent>(start, Options), stopUtc, out truncated);
            }
            catch (EvaluationException)
            {
                truncated = false;
                return [];
            }
        }
    }

    /// <summary>
    /// Applies the per-event cap, the overall backstop cap, and the window's early stop while
    /// materialising. The per-event cap only stops adding that event's own further occurrences —
    /// enumeration carries on so every other event still gets its share. <c>truncated</c> is set
    /// if either cap dropped anything.
    /// </summary>
    private static List<Occurrence> Bounded(IEnumerable<Occurrence> occurrences, DateTime stopUtc, out bool truncated)
    {
        var list = new List<Occurrence>();
        var perEvent = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        truncated = false;

        foreach (var occurrence in occurrences)
        {
            if (occurrence.Period.StartTime.AsUtc > stopUtc) break;

            if (list.Count >= MaxOccurrences)
            {
                truncated = true;
                break;
            }

            object source = occurrence.Source;
            int countSoFar = perEvent.TryGetValue(source, out int n) ? n : 0;
            if (countSoFar >= MaxOccurrencesPerEvent)
            {
                truncated = true;
                continue;
            }

            perEvent[source] = countSoFar + 1;
            list.Add(occurrence);
        }

        return list;
    }
}
