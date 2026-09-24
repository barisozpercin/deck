using static Deck.Shell.Interop.DisplayNative;

namespace Deck.Shell.Display;

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
                bool supported = GetMonitorBrightness(physical.hPhysicalMonitor, out uint min, out _, out uint max) && max > min;
                _monitors.Add(new Monitor(physical.szPhysicalMonitorDescription, physical.hPhysicalMonitor, min, max, supported));
            }

            return true;
        }, IntPtr.Zero);
    }

    /// <summary>Every monitor, in a fixed order; unsupported ones report Percent 0.</summary>
    public IReadOnlyList<MonitorLevel> Read()
    {
        lock (_gate)
        {
            return _monitors.Select(m =>
            {
                if (_disposed || !m.Supported || !GetMonitorBrightness(m.Handle, out _, out uint current, out _))
                    return new MonitorLevel(m.Name, false, 0);

                return new MonitorLevel(m.Name, true, ToPercent(current, m.Min, m.Max));
            }).ToList();
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
                if (m.Supported) SetMonitorBrightness(m.Handle, FromPercent(percents[i], m.Min, m.Max));
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

    private static int ToPercent(uint value, uint min, uint max) =>
        (int)Math.Round((value - min) * 100.0 / (max - min));

    private static uint FromPercent(int percent, uint min, uint max) =>
        min + (uint)Math.Round(Math.Clamp(percent, 0, 100) * (max - min) / 100.0);
}
