using Deck.Shell.Display;

namespace Deck.Shell.Tests;

public class GammaTintTests
{
    [Fact]
    public void Not_applied_until_something_calls_Apply()
    {
        // No test in this suite calls Apply/Reset/Disable: doing so would touch real hardware
        // (SetDeviceGammaRamp/GetDeviceGammaRamp). This only checks the untouched default.
        Assert.False(GammaTint.IsApplied);
    }

    [Fact]
    public void A_neutral_ramp_is_the_identity_windows_reports()
    {
        var ramp = GammaTint.BuildRamp(1, 1, 1);

        Assert.Equal(768, ramp.Length);
        Assert.Equal(32896, ramp[128]);        // red, mid-grey
        Assert.Equal(32896, ramp[256 + 128]);  // green
        Assert.Equal(65535, ramp[512 + 255]);  // blue, white
    }

    [Fact]
    public void The_warm_ramp_keeps_red_and_pulls_down_green_and_blue()
    {
        var ramp = GammaTint.BuildRamp(1.0, GammaTint.Green, GammaTint.Blue);

        Assert.Equal(65535, ramp[255]);
        Assert.Equal(55705, ramp[256 + 255]);  // 65535 × 0.85
        Assert.Equal(42598, ramp[512 + 255]);  // 65535 × 0.65
    }
}
