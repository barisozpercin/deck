using Deck.Shell.Calendars;

namespace Deck.Shell.Tests;

public class CalendarServiceTests
{
    [Fact]
    public void An_event_on_the_horizons_last_day_is_included()
    {
        // Guards Horizon()'s `to` being the exclusive midnight after its last day, not that day's
        // own midnight — the latter would drop every event on the horizon's last calendar day.
        // The expected `to` is computed independently of Horizon() itself (13 months from the
        // start of this month, matching HorizonMonthsForward + 1), so a regression in Horizon()
        // that shifted `to` couldn't slip past this test the way deriving "last day" from
        // Horizon().To itself would.
        var today = DateTime.Today;
        var expectedTo = new DateTime(today.Year, today.Month, 1).AddMonths(13);

        var (from, to) = CalendarService.Horizon();
        Assert.Equal(expectedTo, to);

        DateTime lastDay = expectedTo.AddDays(-1);

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
