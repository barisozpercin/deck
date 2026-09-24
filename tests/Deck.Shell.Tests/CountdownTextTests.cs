using Deck.Shell.Countdowns;

namespace Deck.Shell.Tests;

public class CountdownTextTests
{
    // A Thursday afternoon.
    private static readonly DateTime Now = new(2026, 9, 24, 14, 35, 0);

    [Theory]
    [InlineData(2026, 10, 30, "36 days", "Ahead")]
    [InlineData(2026, 9, 26, "2 days", "Ahead")]
    [InlineData(2026, 9, 25, "tomorrow", "Soon")]
    [InlineData(2026, 9, 24, "today!", "Reached")]
    [InlineData(2026, 9, 23, "+1 day", "Reached")]
    [InlineData(2026, 9, 12, "+12 days", "Past")]
    public void Date_only_countdowns_count_calendar_days(int y, int m, int d, string value, string phase)
    {
        var view = CountdownText.Describe(new DateTime(y, m, d), hasTime: false, Now);

        Assert.Equal(value, view.Value);
        Assert.Equal(phase, view.Phase.ToString());
    }

    [Theory]
    [InlineData("2026-09-27 18:00", "3 days", "Ahead")]     // 76h away
    [InlineData("2026-09-26 12:00", "45h 25m", "Soon")]     // under 48h
    [InlineData("2026-09-24 14:35:30", "1m", "Soon")]       // the last minute rounds up
    [InlineData("2026-09-24 09:23", "+5h 12m", "Reached")]  // passed today
    [InlineData("2026-09-20 10:00", "+4 days", "Past")]
    [InlineData("2026-09-23 14:35", "+1 day", "Past")]      // exactly a day ago
    public void Timed_countdowns_switch_to_hours_under_48h_and_count_up_after(string target, string value, string phase)
    {
        var view = CountdownText.Describe(DateTime.Parse(target, System.Globalization.CultureInfo.InvariantCulture), hasTime: true, Now);

        Assert.Equal(value, view.Value);
        Assert.Equal(phase, view.Phase.ToString());
    }

    [Fact]
    public void Date_line_says_what_is_being_counted_to()
    {
        Assert.Equal("Sat 12 Dec 2026 · 18:00", CountdownText.DateLine(new DateTime(2026, 12, 12, 18, 0, 0), hasTime: true));
        Assert.Equal("Sat 12 Dec 2026", CountdownText.DateLine(new DateTime(2026, 12, 12), hasTime: false));
    }
}
