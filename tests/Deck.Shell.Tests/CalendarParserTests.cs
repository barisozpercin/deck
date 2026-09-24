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
        Assert.Equal(
            "calendar.google.com/…abc.ics",
            CalendarService.Mask("https://calendar.google.com/calendar/ical/me%40x.com/private-0123456789abc/basic_abc.ics"));
        Assert.Equal("not a link", CalendarService.Mask("not a link"));
    }
}
