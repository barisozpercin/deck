namespace Deck.Shell.Countdowns;

/// <summary>A date the user is counting down to. One tile each, like presets.</summary>
internal sealed class Countdown
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Label { get; set; } = "COUNTDOWN";

    /// <summary>Local wall-clock time. Midnight of the day when <see cref="HasTime"/> is false.</summary>
    public DateTime Target { get; set; }

    /// <summary>Whether a time of day was given. Date-only countdowns count in whole days.</summary>
    public bool HasTime { get; set; }
}
