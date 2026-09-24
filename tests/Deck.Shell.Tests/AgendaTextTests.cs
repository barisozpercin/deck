using Deck.Shell.Calendars;

namespace Deck.Shell.Tests;

public class AgendaTextTests
{
    // Thursday 24 Sep 2026, 14:35.
    private static readonly DateTime Now = new(2026, 9, 24, 14, 35, 0);

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0);

    [Theory]
    [InlineData(24, 14, 0, 15, 0, "now · until 15:00", "Now")]
    [InlineData(24, 14, 38, 15, 0, "in 3 min", "Soon")]
    [InlineData(24, 14, 47, 15, 0, "in 12 min", "Later")]
    [InlineData(24, 15, 35, 16, 0, "in 60 min", "Later")]
    [InlineData(24, 18, 0, 19, 0, "18:00", "Later")]
    [InlineData(25, 9, 0, 9, 30, "Tomorrow 09:00", "Later")]
    [InlineData(29, 9, 0, 9, 30, "Tue 09:00", "Later")]
    public void Says_when_in_the_way_a_glance_needs(int day, int h, int m, int endH, int endM, string when, string state)
    {
        var start = At(day, h, m);
        var end = At(day, endH, endM);

        Assert.Equal(when, AgendaText.When(start, end, Now));
        Assert.Equal(state, AgendaText.State(start, end, Now).ToString());
    }

    [Fact]
    public void Upcoming_skips_ended_and_all_day_events_and_sorts()
    {
        var entries = new[]
        {
            new CalendarEntry("Later", At(24, 18), At(24, 19), false, null),
            new CalendarEntry("Ended", At(24, 9), At(24, 10), false, null),
            new CalendarEntry("Holiday", At(24, 0), At(25, 0), true, null),
            new CalendarEntry("Running", At(24, 14), At(24, 15), false, "https://zoom.us/j/1")
        };

        Assert.Equal(new[] { "Running", "Later" }, AgendaText.Upcoming(entries, Now).Select(e => e.Title));
    }
}
