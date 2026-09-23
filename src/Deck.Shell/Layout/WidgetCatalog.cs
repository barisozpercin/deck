namespace Deck.Shell.Layout;

/// <summary>One size a widget can be placed in. Width and height are in grid cells.</summary>
internal sealed record WidgetVariant(string Id, string Label, int Width, int Height);

/// <summary>
/// A kind of tile the deck knows how to show. Built-in kinds can be on the deck once; per-item
/// kinds (presets, shortcuts) get one tile per saved item, told apart by a reference id.
/// </summary>
internal sealed record WidgetKind(string Id, string Title, bool PerItem, IReadOnlyList<WidgetVariant> Variants)
{
    /// <summary>
    /// The size a widget gets unless another is chosen — for every tile that existed before the
    /// library, that's its old size.
    /// </summary>
    public WidgetVariant Default => Variants[0];

    public WidgetVariant? Variant(string id) => Variants.FirstOrDefault(v => v.Id == id);
}

/// <summary>
/// Every widget kind, its sizes and its names, in one place. The layout rules, the library panel
/// and the migration all read from here, so a size can never disagree between them.
/// </summary>
internal static class WidgetCatalog
{
    public const string Standard = "standard";

    public const string PresetActionPrefix = "preset:";

    private static WidgetVariant Cell() => new(Standard, "1×1", 1, 1);

    public static readonly IReadOnlyList<WidgetKind> Kinds =
    [
        new("claude", "Claude", false, [Cell()]),
        new("weather", "Weather", false,
        [
            Cell(),
            new("compact", "1×1 compact", 1, 1),
            new("hourly", "2×1 · next hours", 2, 1)
        ]),
        new("nowplaying", "Now Playing", false,
        [
            Cell(),
            new("wide", "2×1 · art & controls", 2, 1)
        ]),
        new("system", "System", false, [Cell()]),
        new("noise", "Noise", false, [Cell()]),
        new("mic", "Mic", false, [Cell()]),
        new("camera", "Camera", false, [Cell()]),
        new("clock", "World Clock", false, [Cell()]),
        new("mixer", "Mixer", false,
        [
            new("tall", "2×2 · 6 apps", 2, 2),
            new("short", "2×1 · 3 apps", 2, 1)
        ]),
        new("pomodoro", "Pomodoro", false, [Cell()]),
        new("stopwatch", "Stopwatch", false, [Cell()]),
        new("preset", "Preset", true, [Cell()]),
        new("shortcut", "Shortcut", true, [Cell()])
    ];

    public static WidgetKind? Find(string kind) => Kinds.FirstOrDefault(k => k.Id == kind);

    public static WidgetVariant? Find(string kind, string variant) => Find(kind)?.Variant(variant);

    /// <summary>
    /// Which widget a global hotkey action belongs to, so a hotkey can do nothing while its
    /// widget is in the library.
    /// </summary>
    public static (string Kind, string? Ref)? OwnerOf(string action) => action switch
    {
        "mute" => ("mic", null),
        "room" => ("noise", null),
        "pomodoro" => ("pomodoro", null),
        "stopwatch" => ("stopwatch", null),
        "nowplaying" => ("nowplaying", null),
        _ when action.StartsWith(PresetActionPrefix, StringComparison.Ordinal) =>
            ("preset", action[PresetActionPrefix.Length..]),
        _ => null
    };
}
