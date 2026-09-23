using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Deck.Shell.Weather;

internal sealed record HourlyForecast(DateTime Time, double TempC, int Code);

internal sealed record WeatherReading(
    double TempC,
    double FeelsC,
    double HighC,
    double LowC,
    int Code,
    DateTime FetchedAt,
    IReadOnlyList<HourlyForecast> Hours)
{
    /// <summary>
    /// The next few hours after <paramref name="now"/>. Worked out at display time rather than
    /// fetch time, so the list moves on between the 15-minute fetches.
    /// </summary>
    public IReadOnlyList<HourlyForecast> Upcoming(DateTime now, int count) =>
        Hours.Where(h => h.Time > now).Take(count).ToList();
}

/// <summary>
/// Ankara weather from Open-Meteo.
///
/// Chosen over the usual services because it needs no API key and no account — nothing for the
/// user to register for, and no secret to keep out of the repo. The only thing leaving the
/// machine is a fixed pair of coordinates.
/// </summary>
internal sealed class WeatherService : IDisposable
{
    // Ankara.
    private const double Latitude = 39.9334;
    private const double Longitude = 32.8597;

    private static readonly string Url =
        "https://api.open-meteo.com/v1/forecast" +
        $"?latitude={Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
        $"&longitude={Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
        "&current=temperature_2m,apparent_temperature,weather_code" +
        "&daily=temperature_2m_max,temperature_2m_min" +
        "&hourly=temperature_2m,weather_code" +
        "&timezone=Europe%2FIstanbul&forecast_days=2";

    /// <summary>Past this, the reading is shown as stale rather than presented as current.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(90);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public WeatherReading? Latest { get; private set; }
    public string? Error { get; private set; }

    public bool IsStale => Latest is null || DateTime.Now - Latest.FetchedAt > StaleAfter;

    public async Task RefreshAsync()
    {
        try
        {
            string json = await _http.GetStringAsync(Url);

            Latest = Parse(json, DateTime.Now);
            Error = null;
        }
        catch (Exception ex)
        {
            // Keep the last good reading rather than blanking the tile — but the caller marks
            // it stale, so a cached number is never passed off as current.
            Error = ex is HttpRequestException or TaskCanceledException ? "offline" : ex.Message;
        }
    }

    /// <summary>
    /// Reads an Open-Meteo response. Separate from the fetch so it can be tested without the
    /// network. Hours with a missing value are skipped rather than shown as zero.
    /// </summary>
    public static WeatherReading Parse(string json, DateTime fetchedAt)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var current = root.GetProperty("current");
        var daily = root.GetProperty("daily");

        var hours = new List<HourlyForecast>();
        if (root.TryGetProperty("hourly", out var hourly))
        {
            var times = hourly.GetProperty("time");
            var temps = hourly.GetProperty("temperature_2m");
            var codes = hourly.GetProperty("weather_code");
            int count = Math.Min(times.GetArrayLength(), Math.Min(temps.GetArrayLength(), codes.GetArrayLength()));

            for (int i = 0; i < count; i++)
            {
                if (temps[i].ValueKind != JsonValueKind.Number || codes[i].ValueKind != JsonValueKind.Number) continue;
                if (!DateTime.TryParse(times[i].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) continue;

                hours.Add(new HourlyForecast(time, temps[i].GetDouble(), codes[i].GetInt32()));
            }
        }

        return new WeatherReading(
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("apparent_temperature").GetDouble(),
            daily.GetProperty("temperature_2m_max")[0].GetDouble(),
            daily.GetProperty("temperature_2m_min")[0].GetDouble(),
            current.GetProperty("weather_code").GetInt32(),
            fetchedAt,
            hours);
    }

    /// <summary>WMO weather codes, as used by Open-Meteo.</summary>
    public static (string Icon, string Label) Describe(int code) => code switch
    {
        0 => ("☀️", "Clear"),
        1 => ("🌤️", "Mostly clear"),
        2 => ("⛅", "Partly cloudy"),
        3 => ("☁️", "Overcast"),
        45 or 48 => ("🌫️", "Fog"),
        51 or 53 or 55 => ("🌦️", "Drizzle"),
        56 or 57 => ("🌧️", "Freezing drizzle"),
        61 or 63 => ("🌧️", "Rain"),
        65 => ("🌧️", "Heavy rain"),
        66 or 67 => ("🌧️", "Freezing rain"),
        71 or 73 => ("🌨️", "Snow"),
        75 or 77 => ("❄️", "Heavy snow"),
        80 or 81 => ("🌦️", "Showers"),
        82 => ("🌧️", "Heavy showers"),
        85 or 86 => ("🌨️", "Snow showers"),
        95 => ("⛈️", "Thunderstorm"),
        96 or 99 => ("⛈️", "Thunder & hail"),
        _ => ("🌡️", "—")
    };

    public void Dispose() => _http.Dispose();
}
