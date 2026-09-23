using static Deck.Shell.Interop.NativeMethods;

namespace Deck.Shell.Interop;

internal sealed record MonitorSpec(string Device, RECT Bounds, RECT Work, bool IsPrimary)
{
    public bool IsPortrait => Bounds.Height > Bounds.Width;
}

internal static class Monitors
{
    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    public static List<MonitorSpec> All()
    {
        var found = new List<MonitorSpec>();

        // The delegate must stay alive for the duration of the call; keeping it in a local
        // and passing it directly is enough here since EnumDisplayMonitors is synchronous.
        MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref RECT lprc, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                found.Add(new MonitorSpec(
                    mi.szDevice,
                    mi.rcMonitor,
                    mi.rcWork,
                    (mi.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        };

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// The monitor the deck docks to. Prefers an exact device-name match, then the leftmost
    /// portrait monitor, then simply the leftmost. Resolving by geometry rather than by a
    /// hardcoded \\.\DISPLAY3 matters because Windows renumbers devices when displays are
    /// replugged or the machine wakes with a monitor off.
    /// </summary>
    public static MonitorSpec PickDeckMonitor(string? preferredDevice = null)
    {
        var all = All();
        if (all.Count == 0)
            throw new InvalidOperationException("EnumDisplayMonitors returned no monitors.");

        if (!string.IsNullOrWhiteSpace(preferredDevice))
        {
            var exact = all.FirstOrDefault(m =>
                string.Equals(m.Device, preferredDevice, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        var portrait = all.Where(m => m.IsPortrait).OrderBy(m => m.Bounds.left).FirstOrDefault();
        return portrait ?? all.OrderBy(m => m.Bounds.left).First();
    }

    public static string Describe()
    {
        var lines = All().Select(m =>
            $"{m.Device} {m.Bounds} {(m.IsPrimary ? "PRIMARY " : "")}{(m.IsPortrait ? "PORTRAIT" : "landscape")}");
        return string.Join("\n", lines);
    }
}
