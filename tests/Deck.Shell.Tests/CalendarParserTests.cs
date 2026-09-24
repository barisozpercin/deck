using Deck.Shell.Calendars;

namespace Deck.Shell.Tests;

public class CalendarParserTests
{
    // A weekly Tue/Thu standup in London time with one week skipped (EXDATE 29 Sep) and one
    // moved (1 Oct 09:00 → 11:00), an all-day holiday, and a one-off UTC call.
    private const string Fixture = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:deck-tests
        BEGIN:VEVENT
        UID:weekly-1
        DTSTART;TZID=Europe/London:20260901T090000
        DTEND;TZID=Europe/London:20260901T093000
        RRULE:FREQ=WEEKLY;BYDAY=TU,TH
        EXDATE;TZID=Europe/London:20260929T090000
        SUMMARY:Standup
        DESCRIPTION:Join https://meet.google.com/abc-defg-hij
        END:VEVENT
        BEGIN:VEVENT
        UID:weekly-1
        RECURRENCE-ID;TZID=Europe/London:20261001T090000
        DTSTART;TZID=Europe/London:20261001T110000
        DTEND;TZID=Europe/London:20261001T113000
        SUMMARY:Standup (moved)
        END:VEVENT
        BEGIN:VEVENT
        UID:allday-1
        DTSTART;VALUE=DATE:20260925
        DTEND;VALUE=DATE:20260926
        SUMMARY:Holiday
        END:VEVENT
        BEGIN:VEVENT
        UID:utc-1
        DTSTART:20260924T130000Z
        DTEND:20260924T140000Z
        SUMMARY:Utc call
        LOCATION:https://zoom.us/j/123
        END:VEVENT
        END:VCALENDAR
        """;

    private static DateTime Local(int month, int day, int hourUtc, int minuteUtc = 0) =>
        new DateTime(2026, month, day, hourUtc, minuteUtc, 0, DateTimeKind.Utc).ToLocalTime();

    [Fact]
    public void Expands_repeats_skips_and_moves_into_local_time()
    {
        var calendar = CalendarParser.Load(Fixture);

        // Wide enough to be time-zone independent for the machine running the test.
        var entries = CalendarParser.Entries(calendar, new DateTime(2026, 9, 23), new DateTime(2026, 10, 5));

        Assert.Equal(
            new[] { "Standup", "Utc call", "Holiday", "Standup (moved)" },
            entries.Select(e => e.Title));

        // London 09:00 BST is 08:00 UTC.
        Assert.Equal(Local(9, 24, 8), entries[0].Start);
        Assert.Equal(Local(9, 24, 8, 30), entries[0].End);
        Assert.Equal("https://meet.google.com/abc-defg-hij", entries[0].JoinUrl);

        Assert.Equal(Local(9, 24, 13), entries[1].Start);
        Assert.Equal("https://zoom.us/j/123", entries[1].JoinUrl);

        Assert.True(entries[2].AllDay);
        Assert.Equal(new DateTime(2026, 9, 25), entries[2].Start);
        Assert.Equal(new DateTime(2026, 9, 26), entries[2].End);

        Assert.Equal(Local(10, 1, 10), entries[3].Start);
        Assert.Null(entries[3].JoinUrl);

        Assert.DoesNotContain(entries, e => e.Start.Date == new DateTime(2026, 9, 29));
    }

    [Fact]
    public void Events_outside_the_window_are_left_out()
    {
        var calendar = CalendarParser.Load(Fixture);

        var entries = CalendarParser.Entries(calendar, new DateTime(2026, 11, 2), new DateTime(2026, 11, 3));

        Assert.All(entries, e => Assert.True(e.End > new DateTime(2026, 11, 2) && e.Start < new DateTime(2026, 11, 3)));
    }

    [Fact]
    public void Text_that_is_not_a_calendar_is_refused() =>
        Assert.ThrowsAny<Exception>(() => CalendarParser.Load("<html>login required</html>"));

    [Fact]
    public void Links_are_masked_for_display()
    {
        string maskA = CalendarService.Mask("https://calendar.google.com/calendar/ical/a%40x.com/private-1/basic.ics");
        string maskB = CalendarService.Mask("https://calendar.google.com/calendar/ical/b%40x.com/private-2/basic.ics");

        Assert.Matches(@"^calendar\.google\.com · #[0-9a-f]{4}$", maskA);
        Assert.Matches(@"^calendar\.google\.com · #[0-9a-f]{4}$", maskB);
        Assert.NotEqual(maskA, maskB);
        Assert.Equal("(unreadable link)", CalendarService.Mask("not a link"));
    }

    [Fact]
    public void Floating_local_times_are_not_shifted_by_the_machines_utc_offset()
    {
        // No Z and no TZID: RFC 5545 says this is local wall-clock time, whatever machine reads it.
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:deck-tests
            BEGIN:VEVENT
            UID:floating-1
            DTSTART:20260924T090000
            DTEND:20260924T093000
            SUMMARY:Floating call
            END:VEVENT
            END:VCALENDAR
            """;

        var calendar = CalendarParser.Load(ics);

        var entries = CalendarParser.Entries(calendar, new DateTime(2026, 9, 23), new DateTime(2026, 9, 25));

        var entry = Assert.Single(entries);
        Assert.Equal(new DateTime(2026, 9, 24, 9, 0, 0), entry.Start);
        Assert.Equal(new DateTime(2026, 9, 24, 9, 30, 0), entry.End);
    }

    [Fact]
    public void A_rule_that_never_matches_does_not_block_the_rest_of_the_calendar()
    {
        // FREQ=HOURLY;BYMONTH=2;BYMONTHDAY=30 never matches (February never has a 30th) and, left
        // unbounded, Ical.Net searches for a match forever.
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:deck-tests
            BEGIN:VEVENT
            UID:never-1
            DTSTART:20260101T090000Z
            DTEND:20260101T100000Z
            RRULE:FREQ=HOURLY;BYMONTH=2;BYMONTHDAY=30
            SUMMARY:Never
            END:VEVENT
            BEGIN:VEVENT
            UID:normal-1
            DTSTART:20260924T090000Z
            DTEND:20260924T100000Z
            SUMMARY:Normal
            END:VEVENT
            END:VCALENDAR
            """;

        var calendar = CalendarParser.Load(ics);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var entries = CalendarParser.Entries(calendar, new DateTime(2026, 9, 23), new DateTime(2026, 9, 25));
        stopwatch.Stop();

        Assert.Contains(entries, e => e.Title == "Normal");
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void An_all_day_event_just_inside_the_window_end_is_returned()
    {
        // Window ends an hour after local midnight on the event's day, not at the end of that day:
        // this guards the one-day slack before the early-stop in CalendarParser.Entries. It only
        // discriminates on UTC+ machines, which is this desk's; on UTC- it would pass either way.
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:deck-tests
            BEGIN:VEVENT
            UID:allday-boundary
            DTSTART;VALUE=DATE:20260930
            DTEND;VALUE=DATE:20261001
            SUMMARY:Boundary holiday
            END:VEVENT
            END:VCALENDAR
            """;

        var calendar = CalendarParser.Load(ics);

        var entries = CalendarParser.Entries(calendar, new DateTime(2026, 9, 29), new DateTime(2026, 9, 30, 1, 0, 0));

        Assert.Contains(entries, e => e.Title == "Boundary holiday");
    }

    [Fact]
    public void A_meeting_already_running_at_the_window_start_is_returned()
    {
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:deck-tests
            BEGIN:VEVENT
            UID:in-progress
            DTSTART:20260924T060000Z
            DTEND:20260924T220000Z
            SUMMARY:Long call
            END:VEVENT
            END:VCALENDAR
            """;

        var calendar = CalendarParser.Load(ics);

        // fromLocal lands 6 hours into a 16-hour meeting: it began before the window opened but
        // is still running when it does. This guards that an in-progress meeting is returned.
        var entries = CalendarParser.Entries(calendar, Local(9, 24, 12), Local(9, 24, 18));

        Assert.Contains(entries, e => e.Title == "Long call");
    }

    [Fact]
    public void Expand_drops_a_minutely_event_and_still_returns_a_normal_one_quickly()
    {
        // FREQ=MINUTELY from 2000 would cost seconds to expand if Ical.Net had to iterate it; a
        // real calendar app never emits a repeat this chatty, so Expand prunes it before expanding.
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:deck-tests
            BEGIN:VEVENT
            UID:minutely-1
            DTSTART:20000101T090000Z
            DTEND:20000101T091000Z
            RRULE:FREQ=MINUTELY
            SUMMARY:Chatty
            END:VEVENT
            BEGIN:VEVENT
            UID:normal-1
            DTSTART:20260924T090000Z
            DTEND:20260924T100000Z
            SUMMARY:Normal
            END:VEVENT
            END:VCALENDAR
            """;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var entries = CalendarParser.Expand(ics, new DateTime(2026, 9, 1), new DateTime(2026, 10, 1));
        stopwatch.Stop();

        Assert.DoesNotContain(entries, e => e.Title == "Chatty");
        Assert.Contains(entries, e => e.Title == "Normal");
        Assert.True(stopwatch.ElapsedMilliseconds < 3000, $"took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Expand_on_a_feed_with_a_never_matching_rule_returns_the_other_events()
    {
        // FREQ=HOURLY;BYMONTH=2;BYMONTHDAY=30 never matches (February never has a 30th); Expand's
        // load step goes through the same detect-and-retry as CalendarParser.Entries, but now on a
        // calendar instance nobody else has seen yet.
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:deck-tests
            BEGIN:VEVENT
            UID:never-1
            DTSTART:20260101T090000Z
            DTEND:20260101T100000Z
            RRULE:FREQ=HOURLY;BYMONTH=2;BYMONTHDAY=30
            SUMMARY:Never
            END:VEVENT
            BEGIN:VEVENT
            UID:normal-1
            DTSTART:20260924T090000Z
            DTEND:20260924T100000Z
            SUMMARY:Normal
            END:VEVENT
            END:VCALENDAR
            """;

        var entries = CalendarParser.Expand(ics, new DateTime(2026, 9, 23), new DateTime(2026, 9, 25));

        Assert.Contains(entries, e => e.Title == "Normal");
    }
}
