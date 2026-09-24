using static Deck.Shell.Interop.DisplayNative;

namespace Deck.Shell.Display;

/// <summary>
/// One monitor's brightness. <c>Supported</c> is fixed for the process's life: it's whether the
/// monitor answered <c>GetMonitorBrightness</c> at startup, not whether the last read succeeded.
/// <c>Percent</c> is always 0 for an unsupported monitor, and for a supported one is either the
/// value just read or, if that read failed, the last value this process knows about.
/// </summary>
internal sealed record MonitorLevel(string Name, bool Supported, int Percent);

/// <summary>
/// Brightness over DDC/CI: the monitor's own setting, changed down the cable, rather than a
/// software dimmer. Every call is a slow round trip to the monitor's controller (tens of
/// milliseconds), so callers keep this off the UI thread. A lock serialises the calls, since
/// monitors don't like overlapping requests.
/// </summary>
internal sealed class MonitorBrightness : IDisposable
{
    private sealed record Monitor(string Name, IntPtr Handle, uint Min, uint Max, bool Supported);

    private readonly object _gate = new();
    private readonly List<PHYSICAL_MONITOR[]> _groups = [];
    private readonly List<Monitor> _monitors = [];

    /// <summary>Last percent seen for each monitor, by index into <see cref="_monitors"/>; a stale
    /// read falls back to this instead of reporting 0.</summary>
    private readonly List<int> _lastPercent = [];

    private bool _disposed;

    public MonitorBrightness()
    {
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0) return true;

            var group = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, group)) return true;
            _groups.Add(group);

            foreach (var physical in group)
            {
                bool supported = GetMonitorBrightness(physical.hPhysicalMonitor, out uint min, out uint current, out uint max) && max > min;
                _monitors.Add(new Monitor(physical.szPhysicalMonitorDescription, physical.hPhysicalMonitor, min, max, supported));
                _lastPercent.Add(supported ? ToPercent(current, min, max) : 0);
            }

            return true;
        }, IntPtr.Zero);
    }

    /// <summary>
    /// Every monitor, in a fixed order. A supported monitor whose read fails keeps reporting its
    /// last-known percent rather than snapping to 0 — so a drag doesn't mistake a hiccup for the
    /// monitor going dark and send it to minimum on the next <see cref="Set"/>.
    /// </summary>
    public IReadOnlyList<MonitorLevel> Read()
    {
        lock (_gate)
        {
            var levels = new List<MonitorLevel>(_monitors.Count);

            for (int i = 0; i < _monitors.Count; i++)
            {
                var m = _monitors[i];

                if (!_disposed && m.Supported && GetMonitorBrightness(m.Handle, out _, out uint current, out _))
                    _lastPercent[i] = ToPercent(current, m.Min, m.Max);

                levels.Add(new MonitorLevel(m.Name, m.Supported, m.Supported ? _lastPercent[i] : 0));
            }

            return levels;
        }
    }

    /// <summary>One percentage per monitor, in <see cref="Read"/>'s order. Unsupported monitors are skipped.</summary>
    public void Set(IReadOnlyList<int> percents)
    {
        lock (_gate)
        {
            if (_disposed) return;

            for (int i = 0; i < _monitors.Count && i < percents.Count; i++)
            {
                var m = _monitors[i];
                if (!m.Supported) continue;

                int percent = Math.Clamp(percents[i], 0, 100);
                if (SetMonitorBrightness(m.Handle, FromPercent(percent, m.Min, m.Max)))
                    _lastPercent[i] = percent;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var group in _groups) DestroyPhysicalMonitors((uint)group.Length, group);
        }
    }

    /// <summary>Clamped to 0–100: a monitor reporting a current value outside its own min/max must not wrap or overshoot.</summary>
    internal static int ToPercent(uint value, uint min, uint max)
    {
        if (max <= min) return 0;
        return Math.Clamp((int)Math.Round((value - (double)min) * 100.0 / (max - min)), 0, 100);
    }

    internal static uint FromPercent(int percent, uint min, uint max) =>
        min + (uint)Math.Round(Math.Clamp(percent, 0, 100) * (max - min) / 100.0);
}
