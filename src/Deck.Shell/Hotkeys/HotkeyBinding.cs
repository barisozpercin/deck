namespace Deck.Shell.Hotkeys;

/// <summary>
/// A global shortcut for one deck action.
///
/// This exists because of the central awkwardness of a software deck: it lives on a monitor the
/// user isn't looking at, driven by a mouse that's somewhere else. Reflexive actions — muting
/// when someone walks in — are too slow by tile. The screen is for state; the keyboard is for
/// speed.
/// </summary>
internal sealed class HotkeyBinding
{
    /// <summary>"mute", "room", or "preset:0".</summary>
    public string Action { get; set; } = "";

    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }

    /// <summary>Human-readable form, e.g. "Ctrl + Alt + M". Stored so the UI never re-derives it.</summary>
    public string Display { get; set; } = "";

    public bool IsSet => VirtualKey != 0;
}
