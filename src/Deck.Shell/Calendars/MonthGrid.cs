using System.Globalization;

namespace Deck.Shell.Calendars;

internal sealed record MonthDay(DateTime Date, bool InMonth, bool IsToday, IReadOnlyList<string> Events);

internal sealed record MonthView(string Title, IReadOnlyList<MonthDay> Days);

/// <summary>
/// A six-week, Monday-first grid. Always 42 days, so the tile never changes shape between months.
/// </summary>
internal static class MonthGrid
{
    public const int Cells = 42;

    public static DateTime FirstCell(int year, int month)
    {
        var first = new DateTime(year, month, 1);
        int daysSinceMonday = ((int)first.DayOfWeek + 6) % 7;
        return first.AddDays(-daysSinceMonday);
    }

    public static MonthView Build(int year, int month, DateTime today, IReadOnlyList<CalendarEntry> entries)
    {
        var start = FirstCell(year, month);
        var days = new List<MonthDay>(Cells);

        for (int i = 0; i < Cells; i++)
        {
            var date = start.AddDays(i);
            var next = date.AddDays(1);

            var events = entries
                .Where(e => e.Start < next && (e.End > date || e.Start >= date))
                .OrderBy(e => e.Start)
                .Select(e => e.AllDay ? e.Title : e.Start.ToString("HH:mm", CultureInfo.InvariantCulture) + " " + e.Title)
                .ToList();

            days.Add(new MonthDay(date, date.Month == month, date == today.Date, events));
        }

        string title = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        return new MonthView(title, days);
    }
}
