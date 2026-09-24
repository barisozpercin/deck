using Deck.Shell.Calendars;

namespace Deck.Shell.Tests;

public class CalendarServiceTests
{
    [Fact]
    public void An_event_on_the_horizons_last_day_is_included()
    {
        // Guards Horizon()'s `to` being the exclusive midnight after its last day, not that day's
        // own midnight — the latter would drop every event on the horizon's last calendar day.
        var (from, to) = CalendarService.Horizon();
        DateTime lastDay = to.AddDays(-1);

        string ics = $"""
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:deck-tests
            BEGIN:VEVENT
            UID:on-last-day
            DTSTART:{lastDay:yyyyMMdd}T090000
            DTEND:{lastDay:yyyyMMdd}T100000
            SUMMARY:On the last day
            END:VEVENT
            END:VCALENDAR
            """;

        var calendar = CalendarParser.Load(ics);
        var entries = CalendarParser.Entries(calendar, from, to);

        Assert.Contains(entries, e => e.Title == "On the last day");
    }

    [Theory]
    [InlineData("https://example.com/cal.ics", true)]
    [InlineData("webcal://example.com/cal.ics", true)]
    [InlineData("http://example.com/cal.ics", false)]
    [InlineData("not a link", false)]
    public void IsSupportedLink_accepts_https_and_webcal_only(string link, bool expected) =>
        Assert.Equal(expected, CalendarService.IsSupportedLink(link));
}
