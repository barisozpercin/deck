using System.Text.Json;
using Deck.Shell.Config;
using Deck.Shell.Countdowns;
using Deck.Shell.Hotkeys;
using Deck.Shell.Layout;
using Deck.Shell.Presets;

namespace Deck.Shell.Tests;

public class LayoutMigrationTests
{
    private static DeckConfig ConfigWith(int presets, int shortcuts)
    {
        var config = new DeckConfig();
        for (int i = 0; i < presets; i++) config.Presets.Add(new Preset { Name = "P" + i });
        for (int i = 0; i < shortcuts; i++) config.Shortcuts.Add(new DeckShortcut { Label = "S" + i });
        return config;
    }

    [Fact]
    public void Migrates_todays_deck_without_the_timers()
    {
        var config = ConfigWith(presets: 1, shortcuts: 0);

        Assert.True(LayoutMigration.Prepare(config));

        var layout = new DeckLayout(config.Layout);
        Assert.Equal(new WidgetPlacement("preset", "standard", config.Presets[0].Id, 0, 0), layout.At(0, 0));
        Assert.Null(layout.At(1, 0));
        Assert.Equal("claude", layout.At(4, 0)?.Kind);
        Assert.Equal("weather", layout.At(5, 0)?.Kind);
        Assert.Equal("nowplaying", layout.At(0, 1)?.Kind);
        Assert.Equal("system", layout.At(1, 1)?.Kind);
        Assert.Equal("noise", layout.At(2, 1)?.Kind);
        Assert.Equal("mic", layout.At(3, 1)?.Kind);
        Assert.Equal(new WidgetPlacement("mixer", "tall", null, 4, 1), layout.At(5, 2));
        Assert.Equal("clock", layout.At(2, 2)?.Kind);
        Assert.Equal("camera", layout.At(3, 2)?.Kind);
        Assert.Null(layout.At(0, 2));
        Assert.Null(layout.At(1, 2));
        Assert.False(layout.IsPlaced("pomodoro", null));
        Assert.False(layout.IsPlaced("stopwatch", null));
        Assert.True(config.LayoutInitialised);
    }

    [Fact]
    public void Presets_then_shortcuts_fill_four_slots_and_the_rest_wait_in_the_library()
    {
        var config = ConfigWith(presets: 3, shortcuts: 2);

        LayoutMigration.Prepare(config);

        var layout = new DeckLayout(config.Layout);
        Assert.Equal(config.Presets[2].Id, layout.At(2, 0)?.Ref);
        Assert.Equal(config.Shortcuts[0].Id, layout.At(3, 0)?.Ref);
        Assert.False(layout.IsPlaced("shortcut", config.Shortcuts[1].Id));
    }

    [Fact]
    public void Works_with_no_presets_at_all()
    {
        var config = ConfigWith(presets: 0, shortcuts: 0);

        LayoutMigration.Prepare(config);

        Assert.Null(new DeckLayout(config.Layout).At(0, 0));
        Assert.Equal(9, config.Layout.Count);
    }

    [Fact]
    public void Rewrites_preset_hotkeys_from_position_to_id_and_drops_dangling_ones()
    {
        var config = ConfigWith(presets: 2, shortcuts: 0);
        config.Hotkeys.Add(new HotkeyBinding { Action = "preset:1", VirtualKey = 0x31 });
        config.Hotkeys.Add(new HotkeyBinding { Action = "preset:7", VirtualKey = 0x37 });
        config.Hotkeys.Add(new HotkeyBinding { Action = "mute", VirtualKey = 0x4D });

        LayoutMigration.Prepare(config);

        Assert.Equal(new[] { "preset:" + config.Presets[1].Id, "mute" }, config.Hotkeys.Select(h => h.Action));
    }

    [Fact]
    public void Runs_only_once_so_an_emptied_deck_stays_empty()
    {
        var config = ConfigWith(presets: 1, shortcuts: 0);
        LayoutMigration.Prepare(config);
        config.Layout.Clear();

        Assert.False(LayoutMigration.Prepare(config));
        Assert.Empty(config.Layout);
    }

    [Fact]
    public void Drops_tiles_for_presets_deleted_since()
    {
        var config = ConfigWith(presets: 1, shortcuts: 0);
        LayoutMigration.Prepare(config);
        config.Presets.Clear();

        Assert.True(LayoutMigration.Prepare(config));
        Assert.DoesNotContain(config.Layout, p => p.Kind == "preset");
    }

    [Fact]
    public void Layout_and_ids_survive_a_save_and_load()
    {
        var config = ConfigWith(presets: 1, shortcuts: 1);
        LayoutMigration.Prepare(config);

        var loaded = JsonSerializer.Deserialize<DeckConfig>(JsonSerializer.Serialize(config))!;

        Assert.Equal(config.Layout, loaded.Layout);
        Assert.Equal(config.Presets[0].Id, loaded.Presets[0].Id);
        Assert.Equal(config.Shortcuts[0].Id, loaded.Shortcuts[0].Id);
        Assert.False(LayoutMigration.Prepare(loaded));
    }

    [Fact]
    public void Countdown_tiles_survive_validation_only_while_their_countdown_exists()
    {
        var config = ConfigWith(presets: 0, shortcuts: 0);
        LayoutMigration.Prepare(config);
        var trip = new Countdown { Label = "TRIP", Target = new DateTime(2026, 12, 12) };
        config.Countdowns.Add(trip);
        config.Layout.Add(new WidgetPlacement("countdown", "standard", trip.Id, 0, 2));

        Assert.False(LayoutMigration.Prepare(config));
        Assert.True(config.HasItem("countdown", trip.Id));

        config.Countdowns.Clear();

        Assert.True(LayoutMigration.Prepare(config));
        Assert.DoesNotContain(config.Layout, p => p.Kind == "countdown");
        Assert.False(config.HasItem("countdown", trip.Id));
    }
}
