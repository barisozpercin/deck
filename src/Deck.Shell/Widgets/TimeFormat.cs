namespace Deck.Shell.Widgets;

internal static class TimeFormat
{
    /// <summary>Rounded up, so a block that has just started reads 25:00 rather than 24:59.</summary>
    public static string Countdown(TimeSpan remaining) =>
        Clock(TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)));

    public static string Clock(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{value.Minutes:00}:{value.Seconds:00}";
}
