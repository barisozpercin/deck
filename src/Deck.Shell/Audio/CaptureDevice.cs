namespace Deck.Shell.Audio;

/// <param name="Id">Stable MMDevice endpoint id — survives reboots and renaming.</param>
/// <param name="Display">e.g. "Mikrofon (C922 Pro Stream Webcam)"</param>
/// <param name="Hardware">e.g. "C922 Pro Stream Webcam"</param>
internal sealed record CaptureDevice(string Id, string Display, string Hardware)
{
    /// <summary>
    /// Virtual routing channels (Wave Link's Personal/Chat/Stream/Recording/Aux Mix) are filed by
    /// Windows under "recording devices" identically to a real microphone. Muting them would break
    /// streaming and recording silently, so they are never candidates for the mute button.
    /// </summary>
    public bool IsVirtual =>
        Hardware.Contains("Virtual Audio", StringComparison.OrdinalIgnoreCase) ||
        Display.Contains(" Mix", StringComparison.OrdinalIgnoreCase) ||
        Display.Contains("Stereo Mix", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A webcam mic points at the room rather than at a mouth, which makes it the right ambient
    /// sensor and the wrong thing to mute.
    /// </summary>
    public bool LooksLikeRoomSensor =>
        Hardware.Contains("Webcam", StringComparison.OrdinalIgnoreCase) ||
        Hardware.Contains("Cam", StringComparison.OrdinalIgnoreCase);
}
