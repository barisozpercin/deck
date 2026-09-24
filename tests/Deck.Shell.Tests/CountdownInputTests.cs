using Deck.Shell.Countdowns;

namespace Deck.Shell.Tests;

public class CountdownInputTests
{
    [Fact]
    public void A_date_and_time_make_a_timed_countdown()
    {
        Assert.True(CountdownInput.TryCreate("vacation", "2026-12-12", "18:30", out var countdown));

        Assert.Equal("VACATION", countdown.Label);
        Assert.Equal(new DateTime(2026, 12, 12, 18, 30, 0), countdown.Target);
        Assert.True(countdown.HasTime);
    }

    [Fact]
    public void Without_a_time_it_counts_to_the_day()
    {
        Assert.True(CountdownInput.TryCreate("Trip", "2026-12-12", "", out var countdown));

        Assert.Equal(new DateTime(2026, 12, 12), countdown.Target);
        Assert.False(countdown.HasTime);
    }

    [Fact]
    public void A_missing_or_bad_date_is_refused()
    {
        Assert.False(CountdownInput.TryCreate("Trip", "", "10:00", out _));
        Assert.False(CountdownInput.TryCreate("Trip", "12/12/2026", null, out _));
    }

    [Fact]
    public void Names_are_tidied_to_fit_the_tile()
    {
        CountdownInput.TryCreate("   ", "2026-12-12", null, out var unnamed);
        CountdownInput.TryCreate("a very long countdown name", "2026-12-12", null, out var longName);

        Assert.Equal("COUNTDOWN", unnamed.Label);
        Assert.Equal("A VERY LONG CO", longName.Label);
    }
}
