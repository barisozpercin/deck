using System.Runtime.InteropServices;
using static Deck.Shell.Interop.DisplayNative;

namespace Deck.Shell.Display;

/// <summary>
/// The warm reading tint, applied through each display's gamma ramp. Fixed strength, and well
/// inside what Windows accepted on this desk's displays (it refuses ramps too far from neutral).
///
/// It only undoes what it did: <see cref="Reset"/> does nothing unless the deck applied the tint,
/// so it never overwrites another app's calibration.
/// </summary>
internal static class GammaTint
{
    public const double Green = 0.85;
    public const double Blue = 0.65;

    public static bool IsApplied { get; private set; }

    public static void Apply()
    {
        if (SetAll(BuildRamp(1.0, Green, Blue))) IsApplied = true;
    }

    public static void Reset()
    {
        if (!IsApplied) return;

        SetAll(BuildRamp(1.0, 1.0, 1.0));
        IsApplied = false;
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

    /// <summary>Per display, not the whole-screen DC: on a multi-monitor desk only per-display ramps take.</summary>
    private static bool SetAll(ushort[] ramp)
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
                any |= SetDeviceGammaRamp(dc, ramp);
            }
            finally
            {
                DeleteDC(dc);
            }
        }

        return any;
    }
}
