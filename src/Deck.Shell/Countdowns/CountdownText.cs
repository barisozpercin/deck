using System.Globalization;

namespace Deck.Shell.Countdowns;

internal enum CountdownPhase { Ahead, Soon, Reached, Past }

internal readonly record struct CountdownView(string Value, CountdownPhase Phase);

/// <summary>
/// What a countdown tile says. The caller passes "now", so it's pure: every boundary here —
/// midnight, the 48-hour switch to hours, the day after — is the kind of thing that looks fine in
/// a quick check and is wrong on the day.
/// </summary>
internal static class CountdownText
{
    private static readonly TimeSpan HoursThreshold = TimeSpan.FromHours(48);

    public static CountdownView Describe(DateTime target, bool hasTime, DateTime now)
    {
        int days = (target.Date - now.Date).Days;

        if (!hasTime)
        {
            return days switch
            {
                >= 2 => new($"{days} days", CountdownPhase.Ahead),
                1 => new("tomorrow", CountdownPhase.Soon),
                0 => new("today!", CountdownPhase.Reached),
                -1 => new(Since(1), CountdownPhase.Reached),
                _ => new(Since(-days), CountdownPhase.Past)
            };
        }

        var left = target - now;
        if (left > TimeSpan.Zero)
        {
            return left >= HoursThreshold
                ? new($"{days} days", CountdownPhase.Ahead)
                : new(HoursMinutes(left, roundUp: true), CountdownPhase.Soon);
        }

        var since = now - target;
        return since < TimeSpan.FromDays(1)
            ? new("+" + HoursMinutes(since, roundUp: false), CountdownPhase.Reached)
            : new(Since((int)since.TotalDays), CountdownPhase.Past);
    }

    /// <summary>"Sat 12 Dec 2026 · 18:00" — the line under the value, so the tile says what it counts to.</summary>
    public static string DateLine(DateTime target, bool hasTime) =>
        target.ToString(hasTime ? "ddd d MMM yyyy · HH:mm" : "ddd d MMM yyyy", CultureInfo.InvariantCulture);

    private static string Since(int days) => days == 1 ? "+1 day" : $"+{days} days";

    /// <summary>Counting down rounds up, so the last minute reads "1m" rather than "0m"; counting up rounds down.</summary>
    private static string HoursMinutes(TimeSpan span, bool roundUp)
    {
        int minutes = (int)(roundUp ? Math.Ceiling(span.TotalMinutes) : Math.Floor(span.TotalMinutes));
        int hours = minutes / 60;
        int rest = minutes % 60;
        return hours > 0 ? $"{hours}h {rest}m" : $"{rest}m";
    }
}
