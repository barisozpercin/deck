using System.Net.Http;
using System.Text.Json;

namespace Deck.Shell.Weather;

internal sealed record WeatherReading(
    double TempC,
    double FeelsC,
    double HighC,
    double LowC,
    int Code,
    DateTime FetchedAt);

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
        "&timezone=Europe%2FIstanbul&forecast_days=1";

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

            using var document = JsonDocument.Parse(json);
            var current = document.RootElement.GetProperty("current");
            var daily = document.RootElement.GetProperty("daily");

            Latest = new WeatherReading(
                current.GetProperty("temperature_2m").GetDouble(),
                current.GetProperty("apparent_temperature").GetDouble(),
                daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                daily.GetProperty("temperature_2m_min")[0].GetDouble(),
                current.GetProperty("weather_code").GetInt32(),
                DateTime.Now);

            Error = null;
        }
        catch (Exception ex)
        {
            // Keep the last good reading rather than blanking the tile — but the caller marks
            // it stale, so a cached number is never passed off as current.
            Error = ex is HttpRequestException or TaskCanceledException ? "offline" : ex.Message;
        }
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
