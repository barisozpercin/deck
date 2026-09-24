using Deck.Shell.Display;

namespace Deck.Shell.Tests;

public class GammaTintTests
{
    private static readonly ushort[] WarmRamp = GammaTint.BuildRamp(1.0, GammaTint.Green, GammaTint.Blue);

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

    [Fact]
    public void Apply_then_reset_restores_each_displays_captured_original_ramp()
    {
        var originalA = GammaTint.BuildRamp(0.9, 0.9, 0.9);
        var originalB = GammaTint.BuildRamp(1.0, 1.0, 0.8);
        var fake = new FakeDisplays(("A", originalA), ("B", originalB));
        var state = fake.NewState();

        state.Apply();
        Assert.True(state.IsApplied);
        Assert.Equal(WarmRamp, fake.Read("A"));
        Assert.Equal(WarmRamp, fake.Read("B"));

        state.Reset();

        Assert.False(state.IsApplied);
        Assert.Equal(originalA, fake.Read("A"));
        Assert.Equal(originalB, fake.Read("B"));
    }

    [Fact]
    public void A_ramp_already_matching_the_warm_tint_is_never_captured_as_original()
    {
        // Simulates a hard kill while tinted: the process restarts with no memory of ever
        // tinting, but the display's ramp is still the deck's own warm ramp from before the kill.
        var fake = new FakeDisplays(("A", (ushort[])WarmRamp.Clone()));
        var state = fake.NewState();

        state.Apply();
        Assert.True(state.IsApplied);

        state.Reset();

        Assert.False(state.IsApplied);
        Assert.Equal(GammaTint.BuildRamp(1.0, 1.0, 1.0), fake.Read("A"));
    }

    [Fact]
    public void A_reapply_while_tinted_does_not_recapture()
    {
        var original = GammaTint.BuildRamp(0.9, 0.9, 0.9);
        var fake = new FakeDisplays(("A", original));
        int reads = 0;
        var state = new GammaTintState(() => fake.Names, name =>
        {
            reads++;
            return fake.Read(name);
        }, fake.Write);

        state.Apply();
        state.Apply();

        Assert.Equal(1, reads);

        state.Reset();
        Assert.Equal(original, fake.Read("A"));
    }

    [Fact]
    public void A_quantised_warm_ramp_is_still_recognised_as_warm()
    {
        // Some drivers round-trip a written ramp through their own quantisation, so a captured
        // ramp can come back with its low byte zeroed while still meaning "already warm".
        var quantised = new ushort[WarmRamp.Length];
        for (int i = 0; i < WarmRamp.Length; i++) quantised[i] = (ushort)(WarmRamp[i] & 0xFF00);

        var fake = new FakeDisplays(("A", quantised));
        var state = fake.NewState();

        state.Apply();
        state.Reset();

        Assert.False(state.IsApplied);
        Assert.Equal(GammaTint.BuildRamp(1.0, 1.0, 1.0), fake.Read("A"));
    }

    [Fact]
    public void A_ramp_differing_from_warm_by_more_than_the_tolerance_is_saved_and_restored()
    {
        var original = (ushort[])WarmRamp.Clone();
        original[100] += 257;  // one entry, just past WarmTolerance
        var fake = new FakeDisplays(("A", original));
        var state = fake.NewState();

        state.Apply();
        state.Reset();

        Assert.False(state.IsApplied);
        Assert.Equal(original, fake.Read("A"));
    }

    [Fact]
    public void Reset_restores_the_others_when_a_display_disappears_after_apply()
    {
        var originalA = GammaTint.BuildRamp(0.9, 0.9, 0.9);
        var originalB = GammaTint.BuildRamp(0.8, 0.8, 0.8);
        var originalC = GammaTint.BuildRamp(0.7, 0.7, 0.7);
        var fake = new FakeDisplays(("A", originalA), ("B", originalB), ("C", originalC));
        var state = fake.NewState();

        state.Apply();

        // "B" goes away mid-tint (unplugged, put to sleep); its restore write now fails.
        fake.RefuseWrites("B");
        state.Reset();

        Assert.True(state.IsApplied);          // still true, and only because of B
        Assert.Equal(originalA, fake.Read("A"));
        Assert.Equal(originalC, fake.Read("C"));
        Assert.Equal(WarmRamp, fake.Read("B"));
    }

    [Fact]
    public void Reset_retries_only_the_display_that_refused_to_restore()
    {
        var originalA = GammaTint.BuildRamp(0.9, 0.9, 0.9);
        var originalB = GammaTint.BuildRamp(0.8, 0.8, 0.8);
        var fake = new FakeDisplays(("A", originalA), ("B", originalB));
        var state = fake.NewState();

        state.Apply();

        fake.RefuseWrites("B");
        state.Reset();

        Assert.True(state.IsApplied);
        Assert.Equal(originalA, fake.Read("A"));  // restored
        Assert.Equal(WarmRamp, fake.Read("B"));   // refused; still tinted

        fake.AllowWrites("B");
        state.Reset();

        Assert.False(state.IsApplied);
        Assert.Equal(originalB, fake.Read("B"));
    }

    [Fact]
    public void Apply_makes_no_writes_after_disable()
    {
        var original = GammaTint.BuildRamp(0.9, 0.9, 0.9);
        var fake = new FakeDisplays(("A", original));
        int writes = 0;
        var state = new GammaTintState(() => fake.Names, fake.Read, (name, ramp) =>
        {
            writes++;
            return fake.Write(name, ramp);
        });

        state.Apply();
        Assert.True(state.IsApplied);

        state.Disable();
        Assert.False(state.IsApplied);
        Assert.Equal(original, fake.Read("A"));

        int writesAfterDisable = writes;
        state.Apply();

        Assert.Equal(writesAfterDisable, writes);
        Assert.False(state.IsApplied);
    }

    private sealed class FakeDisplays
    {
        private readonly Dictionary<string, ushort[]> _ramps = [];
        private readonly HashSet<string> _refuse = [];

        public FakeDisplays(params (string Name, ushort[] Ramp)[] displays)
        {
            foreach (var (name, ramp) in displays) _ramps[name] = ramp;
        }

        public IReadOnlyList<string> Names => _ramps.Keys.ToList();

        public ushort[]? Read(string name) => _ramps.TryGetValue(name, out var ramp) ? ramp : null;

        public bool Write(string name, ushort[] ramp)
        {
            if (_refuse.Contains(name)) return false;
            _ramps[name] = ramp;
            return true;
        }

        public void RefuseWrites(string name) => _refuse.Add(name);

        public void AllowWrites(string name) => _refuse.Remove(name);

        public GammaTintState NewState() => new(() => Names, Read, Write);
    }
}
