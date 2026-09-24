using System.Runtime.InteropServices;
using static Deck.Shell.Interop.DisplayNative;

namespace Deck.Shell.Display;

/// <summary>
/// The warm reading tint, applied through each display's gamma ramp. Fixed strength, and well
/// inside what Windows accepted on this desk's displays (it refuses ramps too far from neutral).
///
/// It restores what was there before: the first time a display is tinted, its current ramp is
/// saved (another app — a calibration tool, Night Light — may already have one that isn't
/// identity), unless that ramp is already the deck's own warm ramp (a hard kill can leave a
/// display mid-tint, and the next start must not adopt that as the "original"). A display with
/// nothing saved restores to identity. Each display is tracked separately, so a reset that
/// restores some displays but not others leaves the rest tinted and their saves in place — the
/// next reset retries only what's left, rather than losing track of every display at once.
///
/// This is a thin wrapper: the actual state machine is <see cref="GammaTintState"/>, which takes
/// its display access as delegates so it can be driven by tests without touching real hardware.
/// </summary>
internal static class GammaTint
{
    public const double Green = 0.85;
    public const double Blue = 0.65;

    private static readonly GammaTintState _state = new(ListDisplays, ReadRamp, WriteRamp);

    public static bool IsApplied => _state.IsApplied;

    public static void Apply() => _state.Apply();

    public static void Reset() => _state.Reset();

    /// <summary>Blocks any later Apply, then resets. For process teardown, where nothing should re-tint the screens again.</summary>
    public static void Disable() => _state.Disable();

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

    private static IReadOnlyList<string> ListDisplays()
    {
        var names = new List<string>();

        for (uint i = 0; ; i++)
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0) names.Add(device.DeviceName);
        }

        return names;
    }

    private static ushort[]? ReadRamp(string name)
    {
        IntPtr dc = CreateDC(null, name, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;

        try
        {
            var ramp = new ushort[3 * 256];
            return GetDeviceGammaRamp(dc, ramp) ? ramp : null;
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    /// <summary>Per display, not the whole-screen DC: on a multi-monitor desk only per-display ramps take.</summary>
    private static bool WriteRamp(string name, ushort[] ramp)
    {
        IntPtr dc = CreateDC(null, name, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;

        try
        {
            return SetDeviceGammaRamp(dc, ramp);
        }
        finally
        {
            DeleteDC(dc);
        }
    }
}

/// <summary>
/// The tint's capture/restore state machine, independent of Win32 so it can be driven by tests
/// against fake displays. <paramref name="listDisplays"/> reports the currently attached
/// displays by name; <paramref name="readRamp"/> and <paramref name="writeRamp"/> get and set one
/// display's ramp, returning null/false on failure the way the real GDI calls do (the gamma ramp
/// lives in gdi32, not the DDC/CI monitor API the brightness widget talks to).
/// </summary>
internal sealed class GammaTintState(
    Func<IReadOnlyList<string>> listDisplays,
    Func<string, ushort[]?> readRamp,
    Func<string, ushort[], bool> writeRamp)
{
    /// <summary>How far a captured ramp may sit from the deck's own warm ramp and still count as "the same ramp": drivers quantise, so an exact match can't be required.</summary>
    private const int WarmTolerance = 256;

    private static readonly ushort[] WarmRamp = GammaTint.BuildRamp(1.0, GammaTint.Green, GammaTint.Blue);
    private static readonly ushort[] IdentityRamp = GammaTint.BuildRamp(1.0, 1.0, 1.0);

    private readonly object _gate = new();

    /// <summary>Each tinted display's ramp just before the deck tinted it, keyed by device name. A display with nothing here restores to identity.</summary>
    private readonly Dictionary<string, ushort[]> _saved = [];

    /// <summary>Displays the deck believes are currently showing its warm ramp — written by Apply, removed only once Reset has successfully restored them.</summary>
    private readonly HashSet<string> _tinted = [];

    /// <summary>Set by Disable on the way down, so a timer still ticking behind a crash dialog can't re-apply the tint.</summary>
    private bool _disabled;

    public bool IsApplied
    {
        get { lock (_gate) return _tinted.Count > 0; }
    }

    public void Apply()
    {
        lock (_gate)
        {
            if (_disabled) return;

            foreach (var name in listDisplays())
            {
                // Only a display's first tint captures its original — a periodic re-apply while
                // already tinted would otherwise "save" the warm ramp it wrote itself.
                if (!_tinted.Contains(name))
                {
                    var original = readRamp(name);

                    // A hard kill can leave a display mid-tint with no memory of ever tinting it;
                    // adopting that as the "original" would tint it forever. Save nothing instead,
                    // so restoring this display falls back to identity.
                    if (original is not null && !IsWarm(original)) _saved[name] = original;
                }

                if (writeRamp(name, WarmRamp)) _tinted.Add(name);
            }
        }
    }

    public void Reset()
    {
        lock (_gate) ResetLocked();
    }

    public void Disable()
    {
        lock (_gate)
        {
            _disabled = true;
            ResetLocked();
        }
    }

    private void ResetLocked()
    {
        foreach (var name in _tinted.ToArray())
        {
            var original = _saved.TryGetValue(name, out var ramp) ? ramp : IdentityRamp;

            // A display's save (and its tinted flag) is removed only once its restore actually
            // took — a display that refuses stays tinted and saved, so the next Reset retries it
            // instead of losing track of it.
            if (writeRamp(name, original))
            {
                _tinted.Remove(name);
                _saved.Remove(name);
            }
        }
    }

    private static bool IsWarm(ushort[] ramp)
    {
        for (int i = 0; i < WarmRamp.Length; i++)
        {
            if (Math.Abs(ramp[i] - WarmRamp[i]) > WarmTolerance) return false;
        }

        return true;
    }
}
