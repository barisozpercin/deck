using Deck.Shell.Display;

namespace Deck.Shell.Tests;

public class BrightnessShiftTests
{
    [Fact]
    public void Monitors_move_together_and_keep_their_differences()
    {
        // The user's desk: 7%, 100%, 6% — average 38.
        Assert.Equal(new[] { 2, 95, 1 }, BrightnessShift.Apply(new[] { 7, 100, 6 }, target: 33));
    }

    [Fact]
    public void Each_monitor_stops_at_0_and_100_on_its_own()
    {
        Assert.Equal(new[] { 0, 62, 0 }, BrightnessShift.Apply(new[] { 7, 100, 6 }, target: 0));
        Assert.Equal(new[] { 69, 100, 68 }, BrightnessShift.Apply(new[] { 7, 100, 6 }, target: 100));
    }

    [Fact]
    public void Average_rounds_and_handles_no_monitors()
    {
        Assert.Equal(38, BrightnessShift.Average(new[] { 7, 100, 6 }));
        Assert.Equal(0, BrightnessShift.Average(Array.Empty<int>()));
        Assert.Empty(BrightnessShift.Apply(Array.Empty<int>(), 50));
    }
}
