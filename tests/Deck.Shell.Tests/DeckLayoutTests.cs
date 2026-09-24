using Deck.Shell.Layout;

namespace Deck.Shell.Tests;

public class DeckLayoutTests
{
    private const string Std = WidgetCatalog.Standard;

    [Fact]
    public void Places_a_widget_in_a_free_cell()
    {
        var layout = new DeckLayout();

        Assert.True(layout.Place("claude", Std, null, 0, 0));
        Assert.Equal(new WidgetPlacement("claude", Std, null, 0, 0), layout.At(0, 0));
    }

    [Fact]
    public void Refuses_a_second_copy_of_a_built_in_even_in_another_size()
    {
        var layout = new DeckLayout();
        layout.Place("weather", Std, null, 0, 0);

        Assert.False(layout.Place("weather", "compact", null, 1, 0));
    }

    [Fact]
    public void Allows_many_presets_but_not_the_same_one_twice()
    {
        var layout = new DeckLayout();

        Assert.True(layout.Place("preset", Std, "a", 0, 0));
        Assert.True(layout.Place("preset", Std, "b", 1, 0));
        Assert.False(layout.Place("preset", Std, "a", 2, 0));
    }

    [Fact]
    public void Per_item_kinds_need_a_reference_and_built_ins_must_not_have_one()
    {
        var layout = new DeckLayout();

        Assert.False(layout.Place("preset", Std, null, 0, 0));
        Assert.False(layout.Place("claude", Std, "x", 0, 0));
    }

    [Theory]
    [InlineData(5, 0)]   // a 2×1 would run off the right edge
    [InlineData(-1, 0)]
    [InlineData(0, 3)]
    public void Refuses_placements_off_the_grid(int col, int row) =>
        Assert.False(new DeckLayout().Place("weather", "hourly", null, col, row));

    [Fact]
    public void Refuses_unknown_kinds_and_sizes()
    {
        var layout = new DeckLayout();

        Assert.False(layout.Place("toaster", Std, null, 0, 0));
        Assert.False(layout.Place("weather", "huge", null, 0, 0));
    }

    [Fact]
    public void A_2x2_mixer_needs_all_four_cells_free()
    {
        var layout = new DeckLayout();
        layout.Place("camera", Std, null, 5, 2);

        Assert.False(layout.CanPlace("mixer", "tall", null, 4, 1));
        Assert.True(layout.CanPlace("mixer", "tall", null, 3, 1));
    }

    [Fact]
    public void At_finds_a_multi_cell_widget_from_any_of_its_cells()
    {
        var layout = new DeckLayout();
        layout.Place("mixer", "tall", null, 4, 1);

        Assert.Equal("mixer", layout.At(5, 2)?.Kind);
        Assert.Null(layout.At(3, 1));
    }

    [Fact]
    public void Moves_into_free_space()
    {
        var layout = new DeckLayout();
        layout.Place("clock", Std, null, 0, 0);

        Assert.True(layout.Move("clock", null, 3, 2));
        Assert.Null(layout.At(0, 0));
        Assert.Equal("clock", layout.At(3, 2)?.Kind);
    }

    [Fact]
    public void A_move_may_overlap_the_widgets_own_old_cells()
    {
        var layout = new DeckLayout();
        layout.Place("mixer", "short", null, 0, 0);

        Assert.True(layout.Move("mixer", null, 1, 0));
        Assert.Equal(new WidgetPlacement("mixer", "short", null, 1, 0), layout.Find("mixer", null));
    }

    [Fact]
    public void Moving_onto_its_own_position_is_not_a_change()
    {
        var layout = new DeckLayout();
        layout.Place("clock", Std, null, 2, 2);

        Assert.False(layout.Move("clock", null, 2, 2));
    }

    [Fact]
    public void Dropping_on_a_same_size_widget_swaps_them()
    {
        var layout = new DeckLayout();
        layout.Place("clock", Std, null, 0, 0);
        layout.Place("camera", Std, null, 3, 2);

        Assert.True(layout.Move("clock", null, 3, 2));
        Assert.Equal("camera", layout.At(0, 0)?.Kind);
        Assert.Equal("clock", layout.At(3, 2)?.Kind);
    }

    [Fact]
    public void Dropping_on_a_different_size_widget_changes_nothing()
    {
        var layout = new DeckLayout();
        layout.Place("clock", Std, null, 0, 0);
        layout.Place("mixer", "tall", null, 4, 1);

        Assert.False(layout.Move("clock", null, 4, 1));
        Assert.Equal("clock", layout.At(0, 0)?.Kind);
        Assert.Equal("mixer", layout.At(4, 1)?.Kind);
    }

    [Fact]
    public void Removed_built_ins_show_up_as_unplaced()
    {
        var layout = new DeckLayout();
        layout.Place("pomodoro", Std, null, 0, 0);

        Assert.DoesNotContain(layout.UnplacedBuiltIns(), k => k.Id == "pomodoro");
        Assert.True(layout.Remove("pomodoro", null));
        Assert.Contains(layout.UnplacedBuiltIns(), k => k.Id == "pomodoro");
        Assert.DoesNotContain(layout.UnplacedBuiltIns(), k => k.PerItem);
    }

    [Fact]
    public void Validate_drops_what_the_deck_cannot_honour()
    {
        var layout = new DeckLayout(new[]
        {
            new WidgetPlacement("claude", Std, null, 0, 0),
            new WidgetPlacement("claude", Std, null, 1, 0),      // duplicate
            new WidgetPlacement("toaster", Std, null, 2, 0),     // unknown kind
            new WidgetPlacement("weather", "huge", null, 3, 0),  // unknown size
            new WidgetPlacement("preset", Std, "gone", 4, 0),    // preset no longer exists
            new WidgetPlacement("preset", Std, "kept", 5, 0),
            new WidgetPlacement("clock", Std, null, 0, 0),       // overlaps claude
            new WidgetPlacement("mixer", "tall", null, 5, 1),    // off the right edge
            new WidgetPlacement("camera", Std, null, 0, 3)       // off the bottom
        });

        int dropped = layout.Validate((kind, id) => kind == "preset" && id == "kept");

        Assert.Equal(7, dropped);
        Assert.Equal(
            new[]
            {
                new WidgetPlacement("claude", Std, null, 0, 0),
                new WidgetPlacement("preset", Std, "kept", 5, 0)
            },
            layout.Placements);
    }

    [Fact]
    public void Every_hotkey_action_maps_to_its_widget()
    {
        Assert.Equal(("mic", (string?)null), WidgetCatalog.OwnerOf("mute"));
        Assert.Equal(("noise", (string?)null), WidgetCatalog.OwnerOf("room"));
        Assert.Equal(("preset", (string?)"abc"), WidgetCatalog.OwnerOf("preset:abc"));
        Assert.Null(WidgetCatalog.OwnerOf("nonsense"));
    }

    [Fact]
    public void Validate_asks_about_every_per_item_kind_including_countdowns()
    {
        var layout = new DeckLayout(new[]
        {
            new WidgetPlacement("countdown", Std, "trip", 0, 0),
            new WidgetPlacement("countdown", Std, "gone", 1, 0),
            new WidgetPlacement("shortcut", Std, "s1", 2, 0),
            new WidgetPlacement("claude", Std, null, 3, 0)
        });
        var asked = new List<string>();

        int dropped = layout.Validate((kind, id) =>
        {
            asked.Add(kind + ":" + id);
            return id != "gone";
        });

        Assert.Equal(1, dropped);
        Assert.Equal(new[] { "countdown:trip", "countdown:gone", "shortcut:s1" }, asked);
    }

    [Fact]
    public void Agenda_and_month_can_both_be_on_the_deck()
    {
        var layout = new DeckLayout();

        Assert.True(layout.Place("agenda", "wide", null, 0, 0));
        Assert.True(layout.Place("month", Std, null, 2, 0));
        Assert.Equal("month", layout.At(3, 1)?.Kind);
        Assert.False(layout.CanPlace("month", Std, null, 5, 0));
    }
}
