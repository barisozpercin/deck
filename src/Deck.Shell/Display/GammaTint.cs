using System.Runtime.InteropServices;
using static Deck.Shell.Interop.DisplayNative;

namespace Deck.Shell.Display;

/// <summary>
/// The warm reading tint, applied through each display's gamma ramp. Fixed strength, and well
/// inside what Windows accepted on this desk's displays (it refuses ramps too far from neutral).
///
/// It restores what was there before: the first <see cref="Apply"/> after a reset saves each
/// attached display's current ramp (another app — a calibration tool, Night Light — may already
/// have one that isn't identity), and <see cref="Reset"/> puts that back rather than assuming
/// plain identity. A display with nothing saved falls back to identity. A reset that couldn't
/// restore anything (e.g. a locked desktop) leaves the saved ramps and <see cref="IsApplied"/> in
/// place, so the next <see cref="Reset"/> tries again instead of losing them.
/// </summary>
internal static class GammaTint
{
    public const double Green = 0.85;
    public const double Blue = 0.65;

    public static bool IsApplied { get; private set; }

    /// <summary>Each attached display's ramp just before the deck first tinted it, keyed by device name.</summary>
    private static readonly Dictionary<string, ushort[]> _saved = [];

    /// <summary>Set by <see cref="Disable"/> on the way down, so a timer still ticking behind a crash dialog can't re-apply the tint.</summary>
    private static bool _disabled;

    public static void Apply()
    {
        if (_disabled) return;

        // Only the first Apply after a reset captures — a periodic re-apply while already tinted
        // would otherwise "save" the warm ramp itself.
        if (!IsApplied) Capture();

        if (SetAll(BuildRamp(1.0, Green, Blue))) IsApplied = true;
    }

    public static void Reset()
    {
        if (!IsApplied) return;

        if (RestoreAll())
        {
            IsApplied = false;
            _saved.Clear();
        }
    }

    /// <summary>Blocks any later Apply, then resets. For process teardown, where nothing should re-tint the screens again.</summary>
    public static void Disable()
    {
        _disabled = true;
        Reset();
    }

    /// <summary>Red, green then blue, 256 entries each, scaled from the identity ramp.</summary>
    public static ushort[] BuildRamp(double red, double green, double blue)
    {
        var ramp = new ushort[3 * 256];

        for (int i = 0; i < 256; i++)
        {
            int level = i * 257;
            ramp[i] = (ushort)Math.Round(level * red);
            ramp[256 + i] = (ushort)Math.Round(level * green);
            ramp[512 + i] = (ushort)Math.Round(level * blue);
        }

        return ramp;
    }

    private static void Capture()
    {
        _saved.Clear();
        ForEachDisplay((dc, name) =>
        {
            var ramp = new ushort[3 * 256];
            if (GetDeviceGammaRamp(dc, ramp)) _saved[name] = ramp;
            return true;
        });
    }

    private static bool RestoreAll() => ForEachDisplay((dc, name) =>
        SetDeviceGammaRamp(dc, _saved.TryGetValue(name, out var ramp) ? ramp : BuildRamp(1.0, 1.0, 1.0)));

    /// <summary>Per display, not the whole-screen DC: on a multi-monitor desk only per-display ramps take.</summary>
    private static bool SetAll(ushort[] ramp) => ForEachDisplay((dc, _) => SetDeviceGammaRamp(dc, ramp));

    private static bool ForEachDisplay(Func<IntPtr, string, bool> action)
    {
        bool any = false;

        for (uint i = 0; ; i++)
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            IntPtr dc = CreateDC(null, device.DeviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) continue;

            try
            {
                any |= action(dc, device.DeviceName);
            }
            finally
            {
                DeleteDC(dc);
            }
        }

        return any;
    }
}
