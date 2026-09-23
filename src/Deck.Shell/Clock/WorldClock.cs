namespace Deck.Shell.Clock;

internal readonly record struct CityTime(string Label, string Time, int DayOffset, bool IsLocal);

/// <summary>
/// Fixed set of cities. Windows time zone ids are used rather than raw offsets so daylight
/// saving is handled by the OS — London and Berlin shift on different dates from Ankara, which
/// doesn't observe it at all.
/// </summary>
internal static class WorldClock
{
    private static readonly (string Label, string ZoneId)[] Cities =
    [
        ("London", "GMT Standard Time"),
        ("Berlin", "W. Europe Standard Time"),
        ("Ankara", "Turkey Standard Time"),
        ("Melbourne", "AUS Eastern Standard Time")
    ];

    public static IReadOnlyList<CityTime> Now()
    {
        DateTime utc = DateTime.UtcNow;
        DateTime localDate = DateTime.Now.Date;
        string localId = TimeZoneInfo.Local.Id;

        var times = new List<CityTime>(Cities.Length);

        foreach (var (label, zoneId) in Cities)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                var time = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);

                times.Add(new CityTime(
                    label,
                    time.ToString("HH:mm"),
                    (time.Date - localDate).Days,
                    string.Equals(zone.Id, localId, StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                // A zone missing from this Windows install shouldn't blank the whole tile.
                times.Add(new CityTime(label, "--:--", 0, false));
            }
        }

        return times;
    }
}
