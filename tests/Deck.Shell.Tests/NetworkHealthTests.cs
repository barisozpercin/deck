using Deck.Shell.Network;

namespace Deck.Shell.Tests;

public class NetworkHealthTests
{
    [Theory]
    [InlineData(false, 18, 0, "Offline")]
    [InlineData(true, 18, 0, "Good")]
    [InlineData(true, 119, 0, "Good")]
    [InlineData(true, 120, 0, "Slow")]
    [InlineData(true, 18, 1, "Slow")]
    [InlineData(true, 18, 2, "Bad")]
    [InlineData(true, -1, 0, "Good")]      // -1 = no ping has come back yet
    [InlineData(false, -1, 5, "Offline")]  // no adapter beats lost pings
    public void Assess(bool up, int pingMs, int failures, string expected) =>
        Assert.Equal(expected, NetworkHealth.Assess(up, pingMs < 0 ? null : pingMs, failures).ToString());

    [Theory]
    [InlineData(0, "0 kb/s")]
    [InlineData(105_000, "840 kb/s")]
    [InlineData(1_550_000, "12.4 Mb/s")]
    [InlineData(127_500_000, "1.02 Gb/s")]
    [InlineData(-5, "0 kb/s")]
    public void Rates_are_shown_in_bits_like_connections_are_sold(double bytesPerSecond, string expected) =>
        Assert.Equal(expected, NetworkHealth.Rate(bytesPerSecond));
}
