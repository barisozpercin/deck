using Deck.Shell.Calendars;

namespace Deck.Shell.Tests;

public class MonthGridTests
{
    [Fact]
    public void Always_six_monday_first_weeks()
    {
        // 1 Sep 2026 is a Tuesday, so the grid opens on Monday 31 August.
        var view = MonthGrid.Build(2026, 9, today: new DateTime(2026, 9, 24), entries: []);

        Assert.Equal("September 2026", view.Title);
        Assert.Equal(42, view.Days.Count);
        Assert.Equal(new DateTime(2026, 8, 31), view.Days[0].Date);
        Assert.Equal(new DateTime(2026, 10, 11), view.Days[41].Date);
        Assert.Equal(30, view.Days.Count(d => d.InMonth));
        Assert.False(view.Days[0].InMonth);
        Assert.Equal(new DateTime(2026, 8, 31), MonthGrid.FirstCell(2026, 9));
    }

    [Fact]
    public void Marks_today_and_lists_each_days_events()
    {
        var entries = new[]
        {
            new CalendarEntry("Standup", new DateTime(2026, 9, 24, 11, 0, 0), new DateTime(2026, 9, 24, 11, 30, 0), false, null),
            new CalendarEntry("Trip", new DateTime(2026, 9, 25), new DateTime(2026, 9, 28), true, null)
        };

        var view = MonthGrid.Build(2026, 9, today: new DateTime(2026, 9, 24), entries);
        MonthDay Day(int d) => view.Days.Single(x => x.Date == new DateTime(2026, 9, d));

        Assert.True(Day(24).IsToday);
        Assert.Equal(new[] { "11:00 Standup" }, Day(24).Events);
        Assert.Equal(new[] { "Trip" }, Day(25).Events);
        Assert.Equal(new[] { "Trip" }, Day(27).Events);
        Assert.Empty(Day(28).Events);
        Assert.Single(view.Days, d => d.IsToday);
    }

    [Fact]
    public void A_month_starting_on_monday_starts_the_grid_that_day() =>
        Assert.Equal(new DateTime(2026, 6, 1), MonthGrid.FirstCell(2026, 6));
}
