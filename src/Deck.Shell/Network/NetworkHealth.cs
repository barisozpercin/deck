using System.Globalization;

namespace Deck.Shell.Network;

internal enum NetworkState { Good, Slow, Bad, Offline }

/// <summary>Turns raw readings into what the tile says. Pure, so the thresholds are tested rather than eyeballed.</summary>
internal static class NetworkHealth
{
    /// <summary>Above this a call starts to feel it; below it nobody notices.</summary>
    public const int SlowPingMs = 120;

    /// <param name="lastPingMs">The most recent successful ping, or null if none has come back yet.</param>
    /// <param name="consecutiveFailures">
    /// Lost pings in a row. One is a blip worth an amber hint; two is a connection in trouble.
    /// </param>
    public static NetworkState Assess(bool adapterUp, long? lastPingMs, int consecutiveFailures)
    {
        if (!adapterUp) return NetworkState.Offline;
        if (consecutiveFailures >= 2) return NetworkState.Bad;
        if (consecutiveFailures == 1 || lastPingMs >= SlowPingMs) return NetworkState.Slow;
        return NetworkState.Good;
    }

    /// <summary>Bytes per second shown as bits, the unit connections are sold in: "840 kb/s", "12.4 Mb/s", "1.02 Gb/s".</summary>
    public static string Rate(double bytesPerSecond)
    {
        double bits = Math.Max(0, bytesPerSecond) * 8;

        return bits switch
        {
            < 1_000_000 => (bits / 1_000).ToString("0", CultureInfo.InvariantCulture) + " kb/s",
            < 1_000_000_000 => (bits / 1_000_000).ToString("0.0", CultureInfo.InvariantCulture) + " Mb/s",
            _ => (bits / 1_000_000_000).ToString("0.00", CultureInfo.InvariantCulture) + " Gb/s"
        };
    }
}
