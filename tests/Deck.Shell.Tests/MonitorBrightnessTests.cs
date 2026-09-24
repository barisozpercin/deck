using Deck.Shell.Display;

namespace Deck.Shell.Tests;

public class MonitorBrightnessTests
{
    [Theory]
    [InlineData(0u, 10u, 60u, 0)]     // current below min
    [InlineData(200u, 10u, 60u, 100)] // current above max
    public void ToPercent_clamps_a_current_value_outside_its_own_range(uint current, uint min, uint max, int expected) =>
        Assert.Equal(expected, MonitorBrightness.ToPercent(current, min, max));

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(80)]
    [InlineData(100)]
    public void Percent_round_trips_for_min_0_max_100(int percent) =>
        Assert.Equal(percent, MonitorBrightness.ToPercent(MonitorBrightness.FromPercent(percent, 0, 100), 0, 100));

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(80)]
    [InlineData(100)]
    public void Percent_round_trips_for_min_10_max_60(int percent) =>
        Assert.Equal(percent, MonitorBrightness.ToPercent(MonitorBrightness.FromPercent(percent, 10, 60), 10, 60));
}
