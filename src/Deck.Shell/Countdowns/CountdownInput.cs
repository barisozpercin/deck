using System.Globalization;

namespace Deck.Shell.Countdowns;

internal static class CountdownInput
{
    /// <summary>Longest name that still fits a tile's label line.</summary>
    public const int MaxLabel = 14;

    /// <summary>
    /// Builds a countdown from the window's form. The form already insists on a date; this is the
    /// host not trusting it. The result has a fresh id; editing copies its fields onto the
    /// existing countdown instead.
    /// </summary>
    public static bool TryCreate(string? label, string? date, string? time, out Countdown countdown)
    {
        countdown = new Countdown();

        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return false;

        bool hasTime = TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out var at);

        string name = (label ?? "").Trim().ToUpperInvariant();
        if (name.Length == 0) name = "COUNTDOWN";
        if (name.Length > MaxLabel) name = name[..MaxLabel];

        countdown.Label = name;
        countdown.Target = hasTime ? day + at : day;
        countdown.HasTime = hasTime;
        return true;
    }
}
