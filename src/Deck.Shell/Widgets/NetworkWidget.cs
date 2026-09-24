using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Deck.Shell.Network;

namespace Deck.Shell.Widgets;

/// <summary>
/// Ping and live throughput. The ping is the only traffic this creates — the speeds come from the
/// adapters' own counters, so the tile never competes with a call for bandwidth.
/// </summary>
internal sealed class NetworkWidget(WidgetContext context) : WidgetBase(context, "network")
{
    /// <summary>Cloudflare's resolver, by address: no DNS lookup to fail first and muddy the answer.</summary>
    private static readonly IPAddress Target = IPAddress.Parse("1.1.1.1");

    private const int PingTimeoutMs = 1000;

    private readonly Stopwatch _sinceLastRead = new();
    private Ping? _ping;
    private bool _pinging;
    private int _ticks;
    private long? _lastPingMs;
    private int _failures;
    private (bool AnyUp, long Received, long Sent) _last;
    private double _down;
    private double _up;

    public override void Start()
    {
        _ping = new Ping();
        _last = NetworkCounters.Read();
        _sinceLastRead.Restart();
        Context.Tick.Ticked += OnTick;
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        Context.Tick.Ticked -= OnTick;
        Context.Tick.Release();
        _ping?.Dispose();
        _ping = null;
    }

    public override void Push()
    {
        var state = NetworkHealth.Assess(_last.AnyUp, _lastPingMs, _failures);

        Post(new
        {
            state = state.ToString().ToLowerInvariant(),
            ping = state switch
            {
                NetworkState.Offline => "offline",
                NetworkState.Bad => "no reply",
                _ => _lastPingMs is { } ms ? $"{ms} ms" : "–"
            },
            down = NetworkHealth.Rate(_down),
            up = NetworkHealth.Rate(_up)
        });
    }

    private void OnTick()
    {
        var now = NetworkCounters.Read();
        double seconds = _sinceLastRead.Elapsed.TotalSeconds;
        _sinceLastRead.Restart();

        if (seconds > 0)
        {
            // Counters restart when an adapter comes back; a negative step is that, not traffic.
            _down = Math.Max(0, now.Received - _last.Received) / seconds;
            _up = Math.Max(0, now.Sent - _last.Sent) / seconds;
        }

        _last = now;

        // Every other second: often enough to catch a drop, rare enough to be no load at all.
        if (_ticks++ % 2 == 0) _ = PingAsync();

        Push();
    }

    private async Task PingAsync()
    {
        if (_pinging || _ping is not { } ping) return;

        _pinging = true;
        try
        {
            var reply = await ping.SendPingAsync(Target, PingTimeoutMs);
            if (reply.Status == IPStatus.Success)
            {
                _lastPingMs = reply.RoundtripTime;
                _failures = 0;
            }
            else
            {
                _failures++;
            }
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException or ObjectDisposedException)
        {
            // No route, or the tile was removed mid-ping and disposed it.
            _failures++;
        }
        finally
        {
            _pinging = false;
        }
    }
}
