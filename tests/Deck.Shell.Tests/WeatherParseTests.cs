using Deck.Shell.Weather;

namespace Deck.Shell.Tests;

public class WeatherParseTests
{
    private const string Sample = """
        {
          "current": { "temperature_2m": 19.4, "apparent_temperature": 17.2, "weather_code": 3 },
          "daily": { "temperature_2m_max": [20.1], "temperature_2m_min": [13.8] },
          "hourly": {
            "time": ["2026-09-23T14:00", "2026-09-23T15:00", "2026-09-23T16:00", "2026-09-23T17:00",
                     "2026-09-23T18:00", "2026-09-23T19:00", "2026-09-23T20:00", "2026-09-23T21:00"],
            "temperature_2m": [19.0, 19.5, 18.2, 17.0, null, 15.1, 14.0, 13.2],
            "weather_code": [3, 3, 2, 1, 0, 0, 0, 0]
          }
        }
        """;

    [Fact]
    public void Reads_current_conditions_and_todays_range()
    {
        var reading = WeatherService.Parse(Sample, new DateTime(2026, 9, 23, 14, 35, 0));

        Assert.Equal(19.4, reading.TempC);
        Assert.Equal(17.2, reading.FeelsC);
        Assert.Equal(20.1, reading.HighC);
        Assert.Equal(13.8, reading.LowC);
        Assert.Equal(3, reading.Code);
    }

    [Fact]
    public void Upcoming_skips_past_hours_and_gaps_and_stops_at_the_count()
    {
        var reading = WeatherService.Parse(Sample, DateTime.Now);

        var next = reading.Upcoming(new DateTime(2026, 9, 23, 14, 35, 0), 6);

        Assert.Equal(new[] { 15, 16, 17, 19, 20, 21 }, next.Select(h => h.Time.Hour));
        Assert.Equal(19.5, next[0].TempC);
    }

    [Fact]
    public void A_response_without_hourly_data_has_no_hours()
    {
        const string json = """
            {
              "current": { "temperature_2m": 1, "apparent_temperature": 1, "weather_code": 0 },
              "daily": { "temperature_2m_max": [2], "temperature_2m_min": [0] }
            }
            """;

        Assert.Empty(WeatherService.Parse(json, DateTime.Now).Hours);
    }
}
