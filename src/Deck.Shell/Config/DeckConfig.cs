using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deck.Shell.Audio;
using Deck.Shell.Layout;

namespace Deck.Shell.Config;

internal sealed class DeckConfig
{
    /// <summary>Endpoint ids the MUTE tile controls.</summary>
    public List<string> MuteDeviceIds { get; set; } = [];

    /// <summary>Endpoint the room-noise monitor listens on. Deliberately never muted.</summary>
    public string? RoomSensorDeviceId { get; set; }

    /// <summary>Whether a Claude session becoming your turn raises a notification.</summary>
    public bool ClaudeNotifications { get; set; } = true;

    /// <summary>0–100 loudness the room may reach before the alert fires.</summary>
    public double RoomThreshold { get; set; } = 60;

    /// <summary>
    /// Buttons that just launch something. They share the preset row because they're the same
    /// shape of thing: a tile you configure once and then press.
    /// </summary>
    public List<DeckShortcut> Shortcuts { get; set; } = [];

    /// <summary>Whether the starter shortcut has ever been seeded. Deleting it must stick.</summary>
    public bool ShortcutsInitialised { get; set; }

    /// <summary>Captured workspace layouts, in the order they appear on the deck.</summary>
    public List<Presets.Preset> Presets { get; set; } = [];

    /// <summary>
    /// Remembered per-app playback levels, 0–100. Kept for apps whether or not they're running,
    /// so a level survives closing and reopening the app — and so an app can be given a level
    /// before it next launches.
    /// </summary>
    public Dictionary<string, int> MixerLevels { get; set; } = [];

    /// <summary>
    /// Last known executable path per app, so a remembered app can still show its icon while
    /// it isn't running.
    /// </summary>
    public Dictionary<string, string> MixerAppPaths { get; set; } = [];

    /// <summary>Global shortcuts, keyed by action id.</summary>
    public List<Hotkeys.HotkeyBinding> Hotkeys { get; set; } = [];

    /// <summary>Where each widget sits on the deck. Anything not listed here is in the library.</summary>
    public List<WidgetPlacement> Layout { get; set; } = [];

    /// <summary>
    /// Whether the pre-library layout has been migrated. Tracked separately from the layout
    /// itself, so taking every widget off the deck can't bring the old layout back.
    /// </summary>
    public bool LayoutInitialised { get; set; }

    /// <summary>Saved countdowns, one tile each. Anything not placed waits in the library.</summary>
    public List<Countdowns.Countdown> Countdowns { get; set; } = [];

    /// <summary>
    /// iCal addresses for the Agenda and Month tiles. They are secret links: fetched from their own
    /// server and nothing else — never logged, never shown on the deck.
    /// </summary>
    public List<string> CalendarLinks { get; set; } = [];

    /// <summary>"d6", "d20" or "coin".</summary>
    public string DiceMode { get; set; } = "d6";

    /// <summary>Whether the Display tile's warm reading tint is on. Re-applied when that tile starts.</summary>
    public bool DisplayTint { get; set; }

    /// <summary>Whether the item behind a per-item tile (a preset, shortcut or countdown) still exists.</summary>
    public bool HasItem(string kind, string id) => kind switch
    {
        "preset" => Presets.Any(p => p.Id == id),
        "shortcut" => Shortcuts.Any(s => s.Id == id),
        "countdown" => Countdowns.Any(c => c.Id == id),
        _ => false
    };

    /// <summary>
    /// Whether autostart has ever been set up. Tracked separately from whether it is currently
    /// on, so switching it off in the tray sticks instead of being re-enabled at every launch.
    /// </summary>
    public bool AutoStartInitialised { get; set; }

    [JsonIgnore]
    public bool IsFirstRun { get; private set; }

    private static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deck");

    private static string FilePath => Path.Combine(Directory, "config.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static DeckConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<DeckConfig>(File.ReadAllText(FilePath));
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // A corrupt config must not stop the deck starting; fall through to defaults.
        }

        return new DeckConfig { IsFirstRun = true };
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }

    /// <summary>
    /// First-run guess, shown to the user rather than hidden: real microphones become the mute
    /// set, a webcam mic is set aside as the room sensor and excluded from muting — otherwise
    /// muting at night would blind the noise monitor at exactly the moment it matters.
    /// </summary>
    public void ApplyDefaults(IReadOnlyList<CaptureDevice> devices)
    {
        var real = devices.Where(d => !d.IsVirtual).ToList();
        var room = real.FirstOrDefault(d => d.LooksLikeRoomSensor);

        RoomSensorDeviceId = room?.Id;
        MuteDeviceIds = real.Where(d => d.Id != room?.Id).Select(d => d.Id).ToList();

        // If the webcam is the only microphone, muting has to be allowed to touch it.
        if (MuteDeviceIds.Count == 0 && room is not null)
            MuteDeviceIds = [room.Id];
    }
}
