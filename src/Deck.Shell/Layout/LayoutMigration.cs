using Deck.Shell.Config;
using static Deck.Shell.Layout.WidgetCatalog;

namespace Deck.Shell.Layout;

/// <summary>
/// Brings a config written before the widget library up to date, then keeps the saved layout
/// honest on every start.
/// </summary>
internal static class LayoutMigration
{
    /// <summary>The first four cells of the top row used to be reserved for presets and shortcuts.</summary>
    private const int ButtonSlots = 4;

    /// <summary>
    /// Runs the one-time migration if it hasn't run yet, then validation. Returns true when the
    /// config changed and should be saved.
    /// </summary>
    public static bool Prepare(DeckConfig config)
    {
        bool changed = false;

        if (!config.LayoutInitialised)
        {
            RewritePresetHotkeys(config);
            config.Layout = DefaultLayout(
                config.Presets.Select(p => p.Id).ToList(),
                config.Shortcuts.Select(s => s.Id).ToList());
            config.LayoutInitialised = true;
            changed = true;
        }

        var layout = new DeckLayout(config.Layout);
        int dropped = layout.Validate(config.HasItem);

        if (dropped > 0)
        {
            config.Layout = layout.Placements.ToList();
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// The deck as it looked before the library, minus the two timers nobody used: presets then
    /// shortcuts across the top left, the mixer in the bottom-right 2×2, everything else where
    /// it has always been.
    /// </summary>
    public static List<WidgetPlacement> DefaultLayout(IReadOnlyList<string> presetIds, IReadOnlyList<string> shortcutIds)
    {
        var layout = new List<WidgetPlacement>();

        var buttons = presetIds.Select(id => (Kind: "preset", Id: id))
            .Concat(shortcutIds.Select(id => (Kind: "shortcut", Id: id)))
            .Take(ButtonSlots);

        int col = 0;
        foreach (var (kind, id) in buttons) layout.Add(new WidgetPlacement(kind, Standard, id, col++, 0));

        layout.Add(new WidgetPlacement("claude", Standard, null, 4, 0));
        layout.Add(new WidgetPlacement("weather", Standard, null, 5, 0));
        layout.Add(new WidgetPlacement("nowplaying", Standard, null, 0, 1));
        layout.Add(new WidgetPlacement("system", Standard, null, 1, 1));
        layout.Add(new WidgetPlacement("noise", Standard, null, 2, 1));
        layout.Add(new WidgetPlacement("mic", Standard, null, 3, 1));
        layout.Add(new WidgetPlacement("mixer", "tall", null, 4, 1));
        layout.Add(new WidgetPlacement("clock", Standard, null, 2, 2));
        layout.Add(new WidgetPlacement("camera", Standard, null, 3, 2));

        return layout;
    }

    /// <summary>
    /// Hotkeys used to name a preset by its list position ("preset:0"); they now name its id. A
    /// binding pointing past the end of the list was already dead, so it goes.
    /// </summary>
    private static void RewritePresetHotkeys(DeckConfig config)
    {
        foreach (var binding in config.Hotkeys)
        {
            if (!binding.Action.StartsWith(PresetActionPrefix, StringComparison.Ordinal)) continue;
            if (!int.TryParse(binding.Action[PresetActionPrefix.Length..], out int index)) continue;

            binding.Action = index >= 0 && index < config.Presets.Count
                ? PresetActionPrefix + config.Presets[index].Id
                : "";
        }

        config.Hotkeys.RemoveAll(h => h.Action.Length == 0);
    }
}
