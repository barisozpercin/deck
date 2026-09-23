namespace Deck.Shell.Presets;

internal sealed class PresetEntry
{
    public string ExecutablePath { get; set; } = "";
    public string ProcessName { get; set; } = "";

    /// <summary>Title at capture time. A weak signal — used only to break ties.</summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Command line used when the app has to be launched. This is how "two Chrome windows with
    /// preset URLs" is expressed, since a running Chrome window won't reveal its own URL.
    /// </summary>
    public string? LaunchArguments { get; set; }

    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public bool IsMaximized { get; set; }

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

internal sealed class Preset
{
    /// <summary>
    /// Stable identity for the deck layout and hotkeys. List position used to serve, but it
    /// shifts whenever an earlier preset is deleted. Configs written before ids existed get one
    /// on load, and the migration saves it.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "PRESET";
    public List<PresetEntry> Entries { get; set; } = [];
}

internal sealed class RunReport
{
    public int Moved { get; set; }
    public int Launched { get; set; }
    public List<string> Failed { get; } = [];

    public string Summary()
    {
        string text = $"{Moved} moved, {Launched} launched";
        if (Failed.Count > 0) text += $", {Failed.Count} failed";
        return text;
    }
}
