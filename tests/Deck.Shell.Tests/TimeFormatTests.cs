using Deck.Shell.Widgets;

namespace Deck.Shell.Tests;

public class TimeFormatTests
{
    [Fact]
    public void Clock_shows_minutes_and_seconds_then_hours_when_needed()
    {
        Assert.Equal("04:07", TimeFormat.Clock(TimeSpan.FromSeconds(247)));
        Assert.Equal("1:02:03", TimeFormat.Clock(new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void Countdown_rounds_up_so_a_fresh_block_reads_its_full_length()
    {
        Assert.Equal("25:00", TimeFormat.Countdown(TimeSpan.FromMinutes(25) - TimeSpan.FromMilliseconds(300)));
    }
}
