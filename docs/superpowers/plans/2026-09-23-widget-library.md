# Widget Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the deck's hard-coded 6×3 tile grid into an Android-style layout: any widget can be removed to a library, put back in any free space in one of its sizes, and moved by drag — with Pomodoro and Stopwatch starting in the library.

**Architecture:** A pure `DeckLayout` model (tested) decides where widgets may sit and is saved in `config.json`. Each tile's logic moves out of the 1,200-line `MainWindow.xaml.cs` into its own `IWidget` class; a `WidgetHost` starts/stops them as the layout changes, so an off-deck widget is fully off. The WebView page stops hard-coding tiles and builds the grid from a `layout` message; edit mode and the library panel live in the page and ask the host for changes.

**Tech Stack:** .NET 10 WPF (`net10.0-windows10.0.19041.0`, x64), WebView2, NAudio, xUnit 2 for tests, plain HTML/CSS/JS for the deck page.

**Spec:** `docs/superpowers/specs/2026-09-23-widget-library-design.md`

## Global Constraints

- **The deck never takes keyboard focus.** No code may activate the deck window. Anything needing a keyboard opens its own ordinary window (as `CaptureWindow` does). Drag uses pointer capture, which needs no focus.
- **No new comments in `.js` files.** Organisation rule: don't write new comments in JavaScript files. C# and CSS follow the existing style (explanatory `///` summaries on non-obvious *why*).
- Grid is **6 columns × 3 rows**. Coordinates are zero-based `Col`/`Row` of a widget's top-left cell.
- Kinds and variants, exactly: `claude`, `weather` (`standard` 1×1, `compact` 1×1, `hourly` 2×1), `nowplaying` (`standard` 1×1, `wide` 2×1), `system`, `noise`, `mic`, `camera`, `clock`, `mixer` (`tall` 2×2, `short` 2×1), `pomodoro`, `stopwatch`, `preset` (per item), `shortcut` (per item). All others are `standard` 1×1. The first variant listed is the default.
- Page → host messages: `widget:{"kind","ref","msg"}` and `layout:{"op","kind","variant","ref","col","row"}`. Host → page: `{type:"layout", …}` and `{type:"widget", kind, ref, data}`.
- Hotkey action for a preset is `preset:<preset id>` (it used to be `preset:<index>`).
- Build: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q`. Tests: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`.
- **Do not `dotnet run` the deck** while the installed deck is running — it would dock a second AppBar. Check the page in the browser with `ui/dev/harness.html`. The real deck is replaced only in Task 9.
- **Never force-kill the deck** (`Stop-Process`, `taskkill /F`): it leaves a dead reserved strip on the monitor. Use graceful `taskkill /IM Deck.exe` (no `/F`) or ask the user to exit from the tray.
- End every commit message with the trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (pass it as a second `-m`).

## File Structure

```
src/Deck.Shell/
  Layout/
    WidgetCatalog.cs      kinds, variants, sizes, hotkey→widget mapping (single source of truth)
    WidgetPlacement.cs    one placed widget (record, saved in config)
    DeckLayout.cs         placement rules: fit, overlap, one-of-each, move/swap, validate
    LayoutMigration.cs    one-time migration from the pre-library config + validation on load
  Widgets/
    IWidget.cs            the widget contract
    WidgetBase.cs         shared plumbing for widgets
    WidgetContext.cs      what a widget may use (config, notifier, mic, shared services, post)
    WidgetHost.cs         starts/stops widgets to match the layout, routes messages/hotkeys
    WidgetFactory.cs      placement → widget instance
    SharedService.cs      reference-counted start/stop base
    TickService.cs        the shared one-second tick
    MediaService.cs       shared 2 s media poll (Now Playing + Mixer)
    PrivacyService.cs     shared camera/mic capability poll (Mic + Camera)
    TimeFormat.cs         mm:ss formatting for the timers
    ClaudeWidget.cs  WeatherWidget.cs  NowPlayingWidget.cs  MixerWidget.cs  SystemWidget.cs
    NoiseWidget.cs  MicWidget.cs  CameraWidget.cs  ClockWidget.cs  PomodoroWidget.cs
    StopwatchWidget.cs  PresetWidget.cs  ShortcutWidget.cs
  ui/
    deck.html             shell: loads css + scripts (rewritten)
    deck.css              all deck styles (moved out of deck.html)
    widgets.js            per-widget templates and renderers
    deck.js               messaging, grid building, data cache
    edit.js               edit mode, drag, library panel
    dev/harness.html      dev-only fake host (not published)
    dev/harness.js
  MainWindow.xaml.cs      slimmed to window/AppBar/WebView/tray/layout ops (rewritten)
  HotkeyWindow.xaml.cs    preset ids; "(not on deck)" labels
  Presets/Preset.cs       + Id
  Config/DeckShortcut.cs  + Id
  Config/DeckConfig.cs    + Layout, LayoutInitialised
  Media/NowPlaying.cs     + album art, previous track
  Weather/WeatherService.cs + hourly forecast, testable Parse
  Deck.Shell.csproj       InternalsVisibleTo, exclude ui/dev from output
tests/Deck.Shell.Tests/
  Deck.Shell.Tests.csproj
  DeckLayoutTests.cs  LayoutMigrationTests.cs  SharedServiceTests.cs  WidgetHostTests.cs
  TimeFormatTests.cs  WeatherParseTests.cs
README.md
```

---

### Task 1: Test project, widget catalogue and layout rules

**Files:**
- Create: `tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj`
- Create: `src/Deck.Shell/Layout/WidgetCatalog.cs`
- Create: `src/Deck.Shell/Layout/WidgetPlacement.cs`
- Create: `src/Deck.Shell/Layout/DeckLayout.cs`
- Modify: `src/Deck.Shell/Deck.Shell.csproj`
- Test: `tests/Deck.Shell.Tests/DeckLayoutTests.cs`

**Interfaces:**
- Produces:
  - `record WidgetVariant(string Id, string Label, int Width, int Height)`
  - `record WidgetKind(string Id, string Title, bool PerItem, IReadOnlyList<WidgetVariant> Variants)` with `WidgetVariant Default` and `WidgetVariant? Variant(string id)`
  - `static class WidgetCatalog`: `const string Standard = "standard"`, `const string PresetActionPrefix = "preset:"`, `IReadOnlyList<WidgetKind> Kinds`, `WidgetKind? Find(string kind)`, `WidgetVariant? Find(string kind, string variant)`, `(string Kind, string? Ref)? OwnerOf(string action)`
  - `record WidgetPlacement(string Kind, string Variant, string? Ref, int Col, int Row)`
  - `class DeckLayout`: `const int Columns = 6`, `const int Rows = 3`, ctor `DeckLayout(IEnumerable<WidgetPlacement>? placements = null)`, `IReadOnlyList<WidgetPlacement> Placements`, `WidgetPlacement? Find(string kind, string? reference)`, `bool IsPlaced(string kind, string? reference)`, `WidgetPlacement? At(int col, int row)`, `bool CanPlace(string kind, string variant, string? reference, int col, int row)`, `bool Place(...)` (same params), `bool Remove(string kind, string? reference)`, `bool Move(string kind, string? reference, int col, int row)`, `int Validate(IReadOnlySet<string> presetIds, IReadOnlySet<string> shortcutIds)`, `IEnumerable<WidgetKind> UnplacedBuiltIns()`

- [ ] **Step 1: Create the test project**

`tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Must match the app's target: it references the app project directly. -->
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <NoWarn>$(NoWarn);WFO0003</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
    <Using Remove="System.Windows.Forms" />
    <Using Remove="System.Drawing" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Deck.Shell\Deck.Shell.csproj" />
  </ItemGroup>

</Project>
```

In `src/Deck.Shell/Deck.Shell.csproj`, add this item group just before the closing `</Project>`:

```xml
  <ItemGroup>
    <!-- The layout rules and migration are internal; the tests need to reach them. -->
    <InternalsVisibleTo Include="Deck.Shell.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing layout tests**

`tests/Deck.Shell.Tests/DeckLayoutTests.cs`:

```csharp
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

        int dropped = layout.Validate(new HashSet<string> { "kept" }, new HashSet<string>());

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
}
```

- [ ] **Step 3: Run the tests to confirm they fail**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS with errors like `The type or namespace name 'Layout' does not exist in the namespace 'Deck.Shell'`.

- [ ] **Step 4: Write the catalogue**

`src/Deck.Shell/Layout/WidgetCatalog.cs`:

```csharp
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
```

`src/Deck.Shell/Layout/WidgetPlacement.cs`:

```csharp
namespace Deck.Shell.Layout;

/// <summary>
/// One widget on the deck, anchored at its top-left cell (zero-based). Ref tells presets and
/// shortcuts apart and is null for built-in widgets.
/// </summary>
internal sealed record WidgetPlacement(string Kind, string Variant, string? Ref, int Col, int Row);
```

- [ ] **Step 5: Write the layout rules**

`src/Deck.Shell/Layout/DeckLayout.cs`:

```csharp
namespace Deck.Shell.Layout;

/// <summary>
/// The rules for where widgets may sit: inside the 6×3 grid, never overlapping, and each
/// built-in widget at most once. Pure logic with no UI, so every rule is unit-tested. The page
/// only ever asks for a change; this decides.
/// </summary>
internal sealed class DeckLayout
{
    public const int Columns = 6;
    public const int Rows = 3;

    private readonly List<WidgetPlacement> _placements;

    public DeckLayout(IEnumerable<WidgetPlacement>? placements = null) =>
        _placements = placements?.ToList() ?? [];

    public IReadOnlyList<WidgetPlacement> Placements => _placements;

    public WidgetPlacement? Find(string kind, string? reference) =>
        _placements.FirstOrDefault(p => Is(p, kind, reference));

    public bool IsPlaced(string kind, string? reference) => Find(kind, reference) is not null;

    /// <summary>The widget covering a cell, whichever of its cells that is.</summary>
    public WidgetPlacement? At(int col, int row) => _placements.FirstOrDefault(p => Covers(p, col, row));

    public bool CanPlace(string kind, string variant, string? reference, int col, int row) =>
        WidgetCatalog.Find(kind) is { } info
        && info.PerItem == (reference is not null)
        && !IsPlaced(kind, reference)
        && Fits(kind, variant, col, row, ignore: null);

    public bool Place(string kind, string variant, string? reference, int col, int row)
    {
        if (!CanPlace(kind, variant, reference, col, row)) return false;

        _placements.Add(new WidgetPlacement(kind, variant, reference, col, row));
        return true;
    }

    public bool Remove(string kind, string? reference) =>
        _placements.RemoveAll(p => Is(p, kind, reference)) > 0;

    /// <summary>
    /// Moves a widget so its top-left lands on the given cell. If it doesn't fit there but the
    /// cell belongs to a widget of exactly the same size, the two trade places instead.
    /// </summary>
    public bool Move(string kind, string? reference, int col, int row)
    {
        if (Find(kind, reference) is not { } moving) return false;
        if (moving.Col == col && moving.Row == row) return false;

        if (Fits(moving.Kind, moving.Variant, col, row, ignore: moving))
        {
            Replace(moving, moving with { Col = col, Row = row });
            return true;
        }

        if (At(col, row) is not { } other || other == moving) return false;
        if (Size(other) != Size(moving)) return false;

        Replace(moving, moving with { Col = other.Col, Row = other.Row });
        Replace(other, other with { Col = moving.Col, Row = moving.Row });
        return true;
    }

    /// <summary>
    /// Drops every placement the deck can't honour: an unknown kind or size, a second copy of a
    /// built-in, a preset or shortcut that no longer exists, anything off the grid or overlapping
    /// an earlier tile. Dropped widgets simply end up in the library, so a damaged config never
    /// stops the deck starting. Returns how many were dropped.
    /// </summary>
    public int Validate(IReadOnlySet<string> presetIds, IReadOnlySet<string> shortcutIds)
    {
        var kept = new DeckLayout();

        foreach (var p in _placements)
        {
            bool referenceExists = p.Kind switch
            {
                "preset" => p.Ref is not null && presetIds.Contains(p.Ref),
                "shortcut" => p.Ref is not null && shortcutIds.Contains(p.Ref),
                _ => true
            };

            if (referenceExists) kept.Place(p.Kind, p.Variant, p.Ref, p.Col, p.Row);
        }

        int dropped = _placements.Count - kept._placements.Count;
        _placements.Clear();
        _placements.AddRange(kept._placements);
        return dropped;
    }

    /// <summary>Built-in widgets not currently on the deck, in catalogue order.</summary>
    public IEnumerable<WidgetKind> UnplacedBuiltIns() =>
        WidgetCatalog.Kinds.Where(k => !k.PerItem && !IsPlaced(k.Id, null));

    private bool Fits(string kind, string variant, int col, int row, WidgetPlacement? ignore)
    {
        if (WidgetCatalog.Find(kind, variant) is not { } size) return false;
        if (col < 0 || row < 0 || col + size.Width > Columns || row + size.Height > Rows) return false;

        for (int c = col; c < col + size.Width; c++)
        {
            for (int r = row; r < row + size.Height; r++)
            {
                var occupant = At(c, r);
                if (occupant is not null && occupant != ignore) return false;
            }
        }

        return true;
    }

    private static bool Covers(WidgetPlacement p, int col, int row)
    {
        var (width, height) = Size(p);
        return col >= p.Col && col < p.Col + width && row >= p.Row && row < p.Row + height;
    }

    private static (int Width, int Height) Size(WidgetPlacement p) =>
        WidgetCatalog.Find(p.Kind, p.Variant) is { } v ? (v.Width, v.Height) : (1, 1);

    private static bool Is(WidgetPlacement p, string kind, string? reference) =>
        p.Kind == kind && string.Equals(p.Ref, reference, StringComparison.Ordinal);

    private void Replace(WidgetPlacement from, WidgetPlacement to) =>
        _placements[_placements.IndexOf(from)] = to;
}
```

- [ ] **Step 6: Run the tests to confirm they pass**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: `Passed!  - Failed: 0, Passed: 18` (the Theory counts 3).

- [ ] **Step 7: Commit**

```bash
git add tests/Deck.Shell.Tests src/Deck.Shell/Layout src/Deck.Shell/Deck.Shell.csproj
git commit -m "Add widget catalogue and layout rules with tests" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Stable ids for presets and shortcuts, and the one-time migration

**Files:**
- Modify: `src/Deck.Shell/Presets/Preset.cs` (class `Preset`)
- Modify: `src/Deck.Shell/Config/DeckShortcut.cs`
- Modify: `src/Deck.Shell/Config/DeckConfig.cs`
- Create: `src/Deck.Shell/Layout/LayoutMigration.cs`
- Test: `tests/Deck.Shell.Tests/LayoutMigrationTests.cs`

**Interfaces:**
- Consumes: `DeckLayout`, `WidgetPlacement`, `WidgetCatalog` from Task 1.
- Produces:
  - `Preset.Id` and `DeckShortcut.Id`: `string`, a new GUID (`"N"` format) by default.
  - `DeckConfig.Layout : List<WidgetPlacement>` and `DeckConfig.LayoutInitialised : bool`
  - `static class LayoutMigration`: `bool Prepare(DeckConfig config)` (true = config changed, caller saves), `List<WidgetPlacement> DefaultLayout(IReadOnlyList<string> presetIds, IReadOnlyList<string> shortcutIds)`

- [ ] **Step 1: Write the failing migration tests**

`tests/Deck.Shell.Tests/LayoutMigrationTests.cs`:

```csharp
using System.Text.Json;
using Deck.Shell.Config;
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
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS — `LayoutMigration` does not exist, `DeckConfig` has no `Layout`, `Preset` has no `Id`.

- [ ] **Step 3: Give presets and shortcuts ids**

In `src/Deck.Shell/Presets/Preset.cs`, replace the `Preset` class with:

```csharp
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
```

In `src/Deck.Shell/Config/DeckShortcut.cs`, add as the first property of `DeckShortcut`:

```csharp
    /// <summary>Stable identity for the deck layout; see <see cref="Presets.Preset.Id"/>.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

```

- [ ] **Step 4: Add the layout to the config**

In `src/Deck.Shell/Config/DeckConfig.cs`, add `using Deck.Shell.Layout;` after `using Deck.Shell.Audio;`, then add these properties after the `Hotkeys` property:

```csharp
    /// <summary>Where each widget sits on the deck. Anything not listed here is in the library.</summary>
    public List<WidgetPlacement> Layout { get; set; } = [];

    /// <summary>
    /// Whether the pre-library layout has been migrated. Tracked separately from the layout
    /// itself, so taking every widget off the deck can't bring the old layout back.
    /// </summary>
    public bool LayoutInitialised { get; set; }
```

- [ ] **Step 5: Write the migration**

`src/Deck.Shell/Layout/LayoutMigration.cs`:

```csharp
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
        int dropped = layout.Validate(
            config.Presets.Select(p => p.Id).ToHashSet(StringComparer.Ordinal),
            config.Shortcuts.Select(s => s.Id).ToHashSet(StringComparer.Ordinal));

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
```

- [ ] **Step 6: Run the tests to confirm they pass**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: `Passed!  - Failed: 0, Passed: 25`.

- [ ] **Step 7: Commit**

```bash
git add src/Deck.Shell/Presets/Preset.cs src/Deck.Shell/Config src/Deck.Shell/Layout/LayoutMigration.cs tests/Deck.Shell.Tests/LayoutMigrationTests.cs
git commit -m "Give presets and shortcuts stable ids and migrate the old layout" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Shared services and the widget host

**Files:**
- Create: `src/Deck.Shell/Widgets/SharedService.cs`
- Create: `src/Deck.Shell/Widgets/TickService.cs`
- Create: `src/Deck.Shell/Widgets/MediaService.cs`
- Create: `src/Deck.Shell/Widgets/PrivacyService.cs`
- Create: `src/Deck.Shell/Widgets/IWidget.cs`
- Create: `src/Deck.Shell/Widgets/WidgetBase.cs`
- Create: `src/Deck.Shell/Widgets/WidgetContext.cs`
- Create: `src/Deck.Shell/Widgets/WidgetHost.cs`
- Test: `tests/Deck.Shell.Tests/SharedServiceTests.cs`, `tests/Deck.Shell.Tests/WidgetHostTests.cs`

**Interfaces:**
- Consumes: `WidgetPlacement` (Task 1); existing `NowPlaying`, `VolumeMixer`, `CapabilityWatcher`, `CapabilityUse`, `Notifier`, `MicController`, `DeckConfig`.
- Produces:
  - `abstract class SharedService`: `void Acquire()`, `void Release()`, `bool IsRunning`, protected abstract `OnStart()`/`OnStop()`
  - `sealed class TickService : SharedService`: `event Action? Ticked`
  - `sealed class MediaService : SharedService, IDisposable`: `NowPlaying NowPlaying`, `VolumeMixer Mixer`, `event Action? Refreshed`, `Task RefreshAsync()`
  - `sealed class PrivacyService : SharedService`: ctor `(TickService tick)`, `CapabilityUse Camera`, `CapabilityUse Microphone`, `event Action? Changed`
  - `interface IWidget`: `string Kind`, `string? Ref`, `string Variant {get;set;}`, `void Start()`, `void Stop()`, `void Push()`, `bool Handle(string message)`, `bool HandleHotkey(string action)`
  - `abstract class WidgetBase : IWidget`: ctor `(WidgetContext context, string kind, string? reference = null)`, protected `WidgetContext Context`, protected `void Post(object data)`; virtual no-op `Start`/`Stop`/`Handle`/`HandleHotkey`; abstract `Push`
  - `sealed class WidgetContext` (all `required init`): `DeckConfig Config`, `Notifier Notifier`, `MicController Mic`, `TickService Tick`, `MediaService Media`, `PrivacyService Privacy`, `Dispatcher Dispatcher`, `Action<string, string?, object> Post`
  - `sealed class WidgetHost`: ctor `(Func<WidgetPlacement, IWidget?> create, Action<string, string?> reportFailure)`, `void Sync(IReadOnlyList<WidgetPlacement>)`, `void PushAll()`, `bool Route(string kind, string? reference, string message)`, `bool Hotkey(string action)`, `T? Find<T>() where T : class, IWidget`, `void StopAll()`

- [ ] **Step 1: Write the failing tests**

`tests/Deck.Shell.Tests/SharedServiceTests.cs`:

```csharp
using Deck.Shell.Widgets;

namespace Deck.Shell.Tests;

public class SharedServiceTests
{
    private sealed class Counting : SharedService
    {
        public int Starts;
        public int Stops;
        protected override void OnStart() => Starts++;
        protected override void OnStop() => Stops++;
    }

    [Fact]
    public void Starts_with_the_first_user_and_stops_after_the_last()
    {
        var service = new Counting();

        service.Acquire();
        service.Acquire();
        Assert.Equal(1, service.Starts);

        service.Release();
        Assert.Equal(0, service.Stops);
        Assert.True(service.IsRunning);

        service.Release();
        Assert.Equal(1, service.Stops);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void Extra_releases_are_ignored()
    {
        var service = new Counting();

        service.Release();
        Assert.Equal(0, service.Stops);

        service.Acquire();
        Assert.Equal(1, service.Starts);
    }
}
```

`tests/Deck.Shell.Tests/WidgetHostTests.cs`:

```csharp
using Deck.Shell.Layout;
using Deck.Shell.Widgets;

namespace Deck.Shell.Tests;

internal sealed class FakeWidget(string kind, string? reference, bool failOnStart) : IWidget
{
    public string Kind { get; } = kind;
    public string? Ref { get; } = reference;
    public string Variant { get; set; } = "";
    public int Starts;
    public int Stops;
    public int Pushes;
    public List<string> Messages { get; } = [];

    public void Start()
    {
        Starts++;
        if (failOnStart) throw new InvalidOperationException("boom");
    }

    public void Stop() => Stops++;
    public void Push() => Pushes++;

    public bool Handle(string message)
    {
        Messages.Add(message);
        return true;
    }

    public bool HandleHotkey(string action) => action == "hk:" + Kind;
}

public class WidgetHostTests
{
    private readonly List<FakeWidget> _created = [];
    private readonly List<(string Kind, string? Ref)> _failures = [];

    private WidgetHost NewHost(Func<WidgetPlacement, bool>? fails = null) => new(
        p =>
        {
            var widget = new FakeWidget(p.Kind, p.Ref, fails?.Invoke(p) ?? false);
            _created.Add(widget);
            return widget;
        },
        (kind, reference) => _failures.Add((kind, reference)));

    private static WidgetPlacement P(string kind, string? reference = null, string variant = "standard") =>
        new(kind, variant, reference, 0, 0);

    [Fact]
    public void Starts_and_pushes_newly_placed_widgets_with_their_variant()
    {
        var host = NewHost();

        host.Sync([P("mixer", variant: "short")]);

        var widget = Assert.Single(_created);
        Assert.Equal(1, widget.Starts);
        Assert.Equal(1, widget.Pushes);
        Assert.Equal("short", widget.Variant);
    }

    [Fact]
    public void Stops_widgets_that_left_the_deck_and_keeps_the_rest_running()
    {
        var host = NewHost();
        host.Sync([P("clock"), P("mic")]);

        host.Sync([P("mic")]);

        Assert.Equal(1, _created[0].Stops);
        Assert.Equal(0, _created[1].Stops);
        Assert.Equal(1, _created[1].Starts);
        Assert.Equal(2, _created.Count);
    }

    [Fact]
    public void Routes_messages_only_to_widgets_on_the_deck()
    {
        var host = NewHost();
        host.Sync([P("mic")]);

        Assert.True(host.Route("mic", null, "press"));
        Assert.False(host.Route("noise", null, "press"));
        Assert.Equal(new[] { "press" }, _created[0].Messages);
    }

    [Fact]
    public void Presets_are_told_apart_by_reference()
    {
        var host = NewHost();
        host.Sync([P("preset", "a"), P("preset", "b")]);

        host.Route("preset", "b", "press");

        Assert.Empty(_created[0].Messages);
        Assert.Single(_created[1].Messages);
    }

    [Fact]
    public void A_widget_that_fails_to_start_is_reported_and_not_retried_until_replaced()
    {
        var host = NewHost(fails: p => p.Kind == "claude");

        host.Sync([P("claude"), P("clock")]);

        Assert.Equal(new[] { ("claude", (string?)null) }, _failures);
        Assert.False(host.Route("claude", null, "press"));
        Assert.True(host.Route("clock", null, "tick"));

        host.PushAll();
        Assert.Equal(2, _failures.Count);

        host.Sync([P("claude"), P("clock")]);
        Assert.Equal(2, _created.Count);

        host.Sync([P("clock")]);
        host.Sync([P("claude"), P("clock")]);
        Assert.Equal(3, _created.Count);
    }

    [Fact]
    public void Hotkeys_reach_only_the_widget_that_claims_them()
    {
        var host = NewHost();
        host.Sync([P("mic")]);

        Assert.True(host.Hotkey("hk:mic"));
        Assert.False(host.Hotkey("hk:noise"));
    }

    [Fact]
    public void StopAll_stops_everything()
    {
        var host = NewHost();
        host.Sync([P("mic"), P("clock")]);

        host.StopAll();

        Assert.All(_created, w => Assert.Equal(1, w.Stops));
        Assert.False(host.Route("mic", null, "press"));
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS — namespace `Deck.Shell.Widgets` does not exist.

- [ ] **Step 3: Write the shared services**

`src/Deck.Shell/Widgets/SharedService.cs`:

```csharp
namespace Deck.Shell.Widgets;

/// <summary>
/// Something several widgets lean on — a timer, a poll. It runs while at least one widget on the
/// deck is using it and stops when the last one leaves, so "in the library" really means off.
/// </summary>
internal abstract class SharedService
{
    private int _users;

    public bool IsRunning => _users > 0;

    public void Acquire()
    {
        if (_users++ == 0) OnStart();
    }

    public void Release()
    {
        if (_users == 0) return;
        if (--_users == 0) OnStop();
    }

    protected abstract void OnStart();

    protected abstract void OnStop();
}
```

`src/Deck.Shell/Widgets/TickService.cs`:

```csharp
using System.Windows.Threading;

namespace Deck.Shell.Widgets;

/// <summary>The one-second heartbeat the timers, stats, clock and privacy poll share.</summary>
internal sealed class TickService : SharedService
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public event Action? Ticked;

    public TickService() => _timer.Tick += (_, _) => Ticked?.Invoke();

    protected override void OnStart() => _timer.Start();

    protected override void OnStop() => _timer.Stop();
}
```

`src/Deck.Shell/Widgets/MediaService.cs`:

```csharp
using System.Windows.Threading;
using Deck.Shell.Audio;
using Deck.Shell.Media;

namespace Deck.Shell.Widgets;

/// <summary>
/// The two-second media poll behind Now Playing and the Mixer. They read the same Windows
/// sessions on the same beat, so they share one poll that runs while either is on the deck.
/// </summary>
internal sealed class MediaService : SharedService, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Task? _initialise;
    private bool _refreshing;

    public NowPlaying NowPlaying { get; } = new();

    public VolumeMixer Mixer { get; } = new();

    public event Action? Refreshed;

    public MediaService() => _timer.Tick += async (_, _) => await RefreshAsync();

    protected override void OnStart()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    protected override void OnStop() => _timer.Stop();

    public async Task RefreshAsync()
    {
        if (_refreshing) return;

        _refreshing = true;
        try
        {
            await (_initialise ??= NowPlaying.InitialiseAsync());
            await NowPlaying.RefreshAsync();
            Mixer.Refresh();
            Refreshed?.Invoke();
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        Mixer.Dispose();
    }
}
```

`src/Deck.Shell/Widgets/PrivacyService.cs`:

```csharp
using Deck.Shell.Privacy;

namespace Deck.Shell.Widgets;

/// <summary>
/// "Is anything using the camera or microphone?" — polled once a second while the Mic or Camera
/// tile is on the deck. The mic tile uses the microphone half to say what is listening.
/// </summary>
internal sealed class PrivacyService(TickService tick) : SharedService
{
    public CapabilityUse Camera { get; private set; } = CapabilityUse.None;

    public CapabilityUse Microphone { get; private set; } = CapabilityUse.None;

    /// <summary>Raised only on change: this polls every second and the page repaints on every message.</summary>
    public event Action? Changed;

    protected override void OnStart()
    {
        tick.Ticked += Poll;
        tick.Acquire();
        Poll();
    }

    protected override void OnStop()
    {
        tick.Ticked -= Poll;
        tick.Release();
    }

    private void Poll()
    {
        var camera = CapabilityWatcher.Query("webcam");
        var microphone = CapabilityWatcher.Query("microphone");

        if (Same(camera, Camera) && Same(microphone, Microphone)) return;

        Camera = camera;
        Microphone = microphone;
        Changed?.Invoke();
    }

    private static bool Same(CapabilityUse a, CapabilityUse b) =>
        a.InUse == b.InUse && a.Describe() == b.Describe();
}
```

- [ ] **Step 4: Write the widget contract, base and context**

`src/Deck.Shell/Widgets/IWidget.cs`:

```csharp
namespace Deck.Shell.Widgets;

/// <summary>
/// One tile's behaviour. An instance runs once — Start, then Stop. Putting a widget back on the
/// deck creates a fresh one, so nothing a removed widget was doing (a running timer, a disarmed
/// alert) comes back with it.
/// </summary>
internal interface IWidget
{
    string Kind { get; }

    /// <summary>Which preset or shortcut this tile is; null for built-in widgets.</summary>
    string? Ref { get; }

    /// <summary>The size it was placed in, from the catalogue ("standard", "short", …).</summary>
    string Variant { get; set; }

    /// <summary>Placed on the deck: start timers, polls, capture streams.</summary>
    void Start();

    /// <summary>Taken off the deck: stop everything <see cref="Start"/> began.</summary>
    void Stop();

    /// <summary>Send the tile's full current state to the page.</summary>
    void Push();

    /// <summary>A message from this tile on the page. True if it was understood.</summary>
    bool Handle(string message);

    /// <summary>A global hotkey. True if this widget owns the action.</summary>
    bool HandleHotkey(string action);
}
```

`src/Deck.Shell/Widgets/WidgetContext.cs`:

```csharp
using System.Windows.Threading;
using Deck.Shell.Audio;
using Deck.Shell.Config;
using Deck.Shell.Notifications;

namespace Deck.Shell.Widgets;

/// <summary>Everything a widget is allowed to use. Built once by the main window.</summary>
internal sealed class WidgetContext
{
    public required DeckConfig Config { get; init; }

    public required Notifier Notifier { get; init; }

    /// <summary>
    /// Owned by the main window, not a widget: the mic tile, the noise tile and the Microphones
    /// window all read the same devices.
    /// </summary>
    public required MicController Mic { get; init; }

    public required TickService Tick { get; init; }

    public required MediaService Media { get; init; }

    public required PrivacyService Privacy { get; init; }

    /// <summary>For events that arrive off the UI thread — audio notifications, capture callbacks.</summary>
    public required Dispatcher Dispatcher { get; init; }

    /// <summary>Sends one widget's data to the page: kind, reference, data.</summary>
    public required Action<string, string?, object> Post { get; init; }
}
```

`src/Deck.Shell/Widgets/WidgetBase.cs`:

```csharp
using Deck.Shell.Layout;

namespace Deck.Shell.Widgets;

internal abstract class WidgetBase : IWidget
{
    protected WidgetBase(WidgetContext context, string kind, string? reference = null)
    {
        Context = context;
        Kind = kind;
        Ref = reference;
    }

    protected WidgetContext Context { get; }

    public string Kind { get; }

    public string? Ref { get; }

    public string Variant { get; set; } = WidgetCatalog.Standard;

    public virtual void Start()
    {
    }

    public virtual void Stop()
    {
    }

    public abstract void Push();

    public virtual bool Handle(string message) => false;

    public virtual bool HandleHotkey(string action) => false;

    protected void Post(object data) => Context.Post(Kind, Ref, data);
}
```

- [ ] **Step 5: Write the host**

`src/Deck.Shell/Widgets/WidgetHost.cs`:

```csharp
using System.Diagnostics;
using Deck.Shell.Layout;

namespace Deck.Shell.Widgets;

/// <summary>
/// Keeps the running widgets in step with the layout: starts what was placed, stops what was
/// removed, and routes page messages and hotkeys only to widgets actually on the deck. That
/// routing is what makes "in the library" mean "off".
/// </summary>
internal sealed class WidgetHost(Func<WidgetPlacement, IWidget?> create, Action<string, string?> reportFailure)
{
    private readonly Dictionary<(string Kind, string? Ref), IWidget> _running = [];

    /// <summary>
    /// Widgets whose Start threw. Kept so the page keeps showing "failed to start" after a
    /// reload, and so they aren't retried on every layout change — only once re-placed.
    /// </summary>
    private readonly HashSet<(string Kind, string? Ref)> _failed = [];

    public void Sync(IReadOnlyList<WidgetPlacement> placements)
    {
        var wanted = new Dictionary<(string Kind, string? Ref), WidgetPlacement>();
        foreach (var p in placements) wanted.TryAdd((p.Kind, p.Ref), p);

        foreach (var key in _running.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
        {
            StopQuietly(_running[key]);
            _running.Remove(key);
        }

        _failed.RemoveWhere(k => !wanted.ContainsKey(k));

        foreach (var (key, placement) in wanted)
        {
            if (_running.ContainsKey(key) || _failed.Contains(key)) continue;
            if (create(placement) is not { } widget) continue;

            widget.Variant = placement.Variant;

            try
            {
                widget.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Widget {placement.Kind} failed to start: {ex}");
                StopQuietly(widget);
                _failed.Add(key);
                reportFailure(placement.Kind, placement.Ref);
                continue;
            }

            _running[key] = widget;
            widget.Push();
        }
    }

    public void PushAll()
    {
        foreach (var widget in _running.Values) widget.Push();
        foreach (var (kind, reference) in _failed) reportFailure(kind, reference);
    }

    public bool Route(string kind, string? reference, string message) =>
        _running.TryGetValue((kind, reference), out var widget) && widget.Handle(message);

    public bool Hotkey(string action) => _running.Values.Any(w => w.HandleHotkey(action));

    public T? Find<T>() where T : class, IWidget => _running.Values.OfType<T>().FirstOrDefault();

    public void StopAll()
    {
        foreach (var widget in _running.Values) StopQuietly(widget);
        _running.Clear();
    }

    private static void StopQuietly(IWidget widget)
    {
        try
        {
            widget.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Widget {widget.Kind} failed to stop: {ex}");
        }
    }
}
```

- [ ] **Step 6: Run the tests to confirm they pass**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: `Passed!  - Failed: 0, Passed: 34`.

- [ ] **Step 7: Commit**

```bash
git add src/Deck.Shell/Widgets tests/Deck.Shell.Tests
git commit -m "Add shared services and the widget host" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Port the simple widgets

Moves logic out of `MainWindow.xaml.cs` into widget classes. `MainWindow` is **not** changed yet; the new classes only need to compile (Task 6 wires them in). Behaviour must match today's exactly — the code below is the existing `MainWindow` logic re-homed.

**Files:**
- Create: `src/Deck.Shell/Widgets/TimeFormat.cs`
- Create: `src/Deck.Shell/Widgets/ClaudeWidget.cs`, `WeatherWidget.cs`, `SystemWidget.cs`, `ClockWidget.cs`, `MicWidget.cs`, `CameraWidget.cs`, `PomodoroWidget.cs`, `StopwatchWidget.cs`, `PresetWidget.cs`, `ShortcutWidget.cs`
- Test: `tests/Deck.Shell.Tests/TimeFormatTests.cs`

**Interfaces:**
- Consumes: `WidgetBase`, `WidgetContext`, `TickService`, `PrivacyService` (Task 3); `WidgetCatalog.PresetActionPrefix` (Task 1); `Preset.Id`, `DeckShortcut.Id` (Task 2).
- Produces: widget classes, each with ctor `(WidgetContext context)` (preset/shortcut: `(WidgetContext context, string id)`). Page messages each handles, and the data it posts:

| Class | Kind | Handles | Hotkey | Posts |
|---|---|---|---|---|
| `ClaudeWidget` | claude | `press`, `notify-toggle` | — | `{waiting, working, names, notify}` |
| `WeatherWidget` | weather | — | — | `{available, icon, label, temp, high, low, feels, stale, error}` |
| `SystemWidget` | system | — | — | `{cpu, ram, gpuAvailable, gpu}` |
| `ClockWidget` | clock | — | — | `{cities:[{label,time,day,local}]}` |
| `MicWidget` | mic | `press` | `mute` | `{muted, devices[], apps}` |
| `CameraWidget` | camera | — | — | `{inUse, apps}` |
| `PomodoroWidget` | pomodoro | `press`, `reset` | `pomodoro` | `{phase, remaining, blocks, awaiting}` |
| `StopwatchWidget` | stopwatch | `press`, `reset` | `stopwatch` | `{running, elapsed, hasElapsed}` |
| `PresetWidget` | preset | `press` | `preset:<id>` | `{name, count, state, summary}` |
| `ShortcutWidget` | shortcut | `press` | — | `{label, note}` |

- `static class TimeFormat`: `string Countdown(TimeSpan)`, `string Clock(TimeSpan)`

- [ ] **Step 1: Write the failing TimeFormat test**

`tests/Deck.Shell.Tests/TimeFormatTests.cs`:

```csharp
using Deck.Shell.Widgets;

namespace Deck.Shell.Tests;

public class TimeFormatTests
{
    [Fact]
    public void Clock_shows_minutes_and_seconds_then_hours_when_needed()
    {
        Assert.Equal("04:07", TimeFormat.Clock(TimeSpan.FromSeconds(247)));
        Assert.Equal("1:02:03", TimeFormat.Clock(new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void Countdown_rounds_up_so_a_fresh_block_reads_its_full_length()
    {
        Assert.Equal("25:00", TimeFormat.Countdown(TimeSpan.FromMinutes(25) - TimeSpan.FromMilliseconds(300)));
    }
}
```

- [ ] **Step 2: Run it to confirm it fails**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS — `TimeFormat` does not exist.

- [ ] **Step 3: Write TimeFormat**

`src/Deck.Shell/Widgets/TimeFormat.cs`:

```csharp
namespace Deck.Shell.Widgets;

internal static class TimeFormat
{
    /// <summary>Rounded up, so a block that has just started reads 25:00 rather than 24:59.</summary>
    public static string Countdown(TimeSpan remaining) =>
        Clock(TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)));

    public static string Clock(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{value.Minutes:00}:{value.Seconds:00}";
}
```

- [ ] **Step 4: Run it to confirm it passes**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: `Passed!  - Failed: 0, Passed: 36`.

- [ ] **Step 5: Write the Claude and Weather widgets**

`src/Deck.Shell/Widgets/ClaudeWidget.cs`:

```csharp
using System.Windows.Threading;
using Deck.Shell.ClaudeStatus;
using Deck.Shell.Interop;

namespace Deck.Shell.Widgets;

/// <summary>What the Claude Code sessions are doing, and a jump to the one that wants you.</summary>
internal sealed class ClaudeWidget(WidgetContext context) : WidgetBase(context, "claude")
{
    private readonly ClaudeWatcher _watcher = new();
    private readonly HashSet<string> _previouslyWaiting = new(StringComparer.Ordinal);
    private DispatcherTimer? _timer;
    private int _focusIndex;
    private bool _polling;
    private bool _firstPoll = true;

    public override void Start()
    {
        // Two seconds: fast enough to notice a turn ending, slow enough that tailing several
        // multi-megabyte transcripts costs nothing worth measuring.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => _ = PollAsync();
        _timer.Start();

        _ = PollAsync();
    }

    public override void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                FocusNextSession();
                return true;

            case "notify-toggle":
                Context.Config.ClaudeNotifications = !Context.Config.ClaudeNotifications;
                Context.Config.Save();
                Push();
                return true;

            default:
                return false;
        }
    }

    public override void Push()
    {
        var sessions = _watcher.Sessions;
        var waiting = sessions.Where(s => !s.Working).ToArray();
        var working = sessions.Where(s => s.Working).ToArray();

        Post(new
        {
            waiting = waiting.Length,
            working = working.Length,
            // Waiting sessions are named first: that's the state that needs you to do something.
            names = string.Join(" · ", waiting.Concat(working).Select(s => s.Name)),
            notify = Context.Config.ClaudeNotifications
        });
    }

    private async Task PollAsync()
    {
        if (_polling || _timer is null) return;

        _polling = true;
        try
        {
            // Reads the disk — the first pass of the day walks today's whole transcripts, so
            // it must not run on the UI thread.
            await Task.Run(_watcher.Poll);

            // Taken off the deck while the poll was running.
            if (_timer is null) return;

            NotifyNewlyWaiting();
            Push();
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>
    /// The tile only helps if you look at it, and you're usually looking at another monitor —
    /// so a session becoming your problem is worth a notification.
    /// </summary>
    private void NotifyNewlyWaiting()
    {
        var waiting = _watcher.Sessions
            .Where(s => !s.Working)
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Don't announce everything that happens to be idle when the widget starts.
        if (!_firstPoll && Context.Config.ClaudeNotifications)
        {
            foreach (string name in waiting.Where(n => !_previouslyWaiting.Contains(n)))
                Context.Notifier.Show("Claude is waiting", $"{name} finished and wants your review.");
        }

        // The seen-set is updated even while muted, so unmuting doesn't dump a backlog of
        // notifications for sessions that went quiet an hour ago.
        _firstPoll = false;
        _previouslyWaiting.Clear();
        foreach (string name in waiting) _previouslyWaiting.Add(name);
    }

    /// <summary>Repeated presses cycle, so two waiting sessions are both reachable.</summary>
    private void FocusNextSession()
    {
        // false sorts before true, so sessions waiting on you come first.
        var ordered = _watcher.Sessions.OrderBy(s => s.Working).ToArray();
        if (ordered.Length == 0) return;

        var target = ordered[_focusIndex % ordered.Length];
        _focusIndex++;

        if (!WindowFocus.FocusProcessWindow(target.Pid))
            Context.Notifier.Show("Couldn't switch", $"No window found for {target.Name}.");
    }
}
```

`src/Deck.Shell/Widgets/WeatherWidget.cs`:

```csharp
using System.Windows.Threading;
using Deck.Shell.Weather;

namespace Deck.Shell.Widgets;

internal sealed class WeatherWidget(WidgetContext context) : WidgetBase(context, "weather")
{
    private readonly WeatherService _weather = new();
    private DispatcherTimer? _timer;

    public override void Start()
    {
        // Weather moves slowly and the service is free — 15 minutes is plenty and stays polite.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    public override void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _weather.Dispose();
    }

    public override void Push()
    {
        var reading = _weather.Latest;
        var (icon, label) = reading is null
            ? ("🌡️", "—")
            : WeatherService.Describe(reading.Code);

        Post(new
        {
            available = reading is not null,
            icon,
            label,
            temp = reading is null ? "–" : $"{Math.Round(reading.TempC)}°",
            high = reading is null ? "" : $"{Math.Round(reading.HighC)}°",
            low = reading is null ? "" : $"{Math.Round(reading.LowC)}°",
            feels = reading is null ? "" : $"{Math.Round(reading.FeelsC)}°",
            // Stale is surfaced rather than hidden: a cached number shown as current is the
            // same lying-tile problem as a mute button that didn't mute.
            stale = _weather.IsStale,
            error = _weather.Error
        });
    }

    private async Task RefreshAsync()
    {
        await _weather.RefreshAsync();
        if (_timer is not null) Push();
    }
}
```

- [ ] **Step 6: Write the System, Clock, Mic and Camera widgets**

`src/Deck.Shell/Widgets/SystemWidget.cs`:

```csharp
using Deck.Shell.Stats;

namespace Deck.Shell.Widgets;

internal sealed class SystemWidget(WidgetContext context) : WidgetBase(context, "system")
{
    private readonly SystemStats _system = new();
    private GpuStats? _gpu;

    public override void Start()
    {
        _gpu = new GpuStats();
        Context.Tick.Ticked += Sample;
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        Context.Tick.Ticked -= Sample;
        Context.Tick.Release();
        _gpu?.Dispose();
        _gpu = null;
    }

    public override void Push()
    {
        bool hasGpu = _gpu is { IsAvailable: true };

        Post(new
        {
            cpu = Math.Round(_system.CpuPercent),
            ram = Math.Round(_system.RamPercent),
            gpuAvailable = hasGpu,
            gpu = hasGpu ? Math.Round(_gpu!.GpuPercent) : 0
        });
    }

    private void Sample()
    {
        _system.Sample();
        _gpu?.Sample();
        Push();
    }
}
```

`src/Deck.Shell/Widgets/ClockWidget.cs`:

```csharp
using Deck.Shell.Clock;

namespace Deck.Shell.Widgets;

internal sealed class ClockWidget(WidgetContext context) : WidgetBase(context, "clock")
{
    private string _lastShown = "";

    public override void Start()
    {
        Context.Tick.Ticked += OnTick;
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        Context.Tick.Ticked -= OnTick;
        Context.Tick.Release();
    }

    public override void Push() => Send(force: true);

    private void OnTick() => Send(force: false);

    private void Send(bool force)
    {
        var cities = WorldClock.Now();
        string shown = string.Join("|", cities.Select(c => $"{c.Time}{c.DayOffset}{c.IsLocal}"));

        // Ticks every second but the display only changes once a minute.
        if (!force && shown == _lastShown) return;
        _lastShown = shown;

        Post(new
        {
            cities = cities.Select(c => new
            {
                label = c.Label,
                time = c.Time,
                day = c.DayOffset,
                local = c.IsLocal
            })
        });
    }
}
```

`src/Deck.Shell/Widgets/MicWidget.cs`:

```csharp
namespace Deck.Shell.Widgets;

/// <summary>
/// The mute button. Taking it off the deck leaves the microphone exactly as it is — a layout
/// edit never changes hardware state.
/// </summary>
internal sealed class MicWidget(WidgetContext context) : WidgetBase(context, "mic")
{
    public override void Start()
    {
        Context.Mic.StateChanged += OnMicChanged;
        Context.Privacy.Changed += Push;
        Context.Privacy.Acquire();
    }

    public override void Stop()
    {
        Context.Mic.StateChanged -= OnMicChanged;
        Context.Privacy.Changed -= Push;
        Context.Privacy.Release();
    }

    public override bool Handle(string message)
    {
        if (message != "press") return false;

        Context.Mic.Toggle();
        return true;
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "mute") return false;

        Context.Mic.Toggle();
        return true;
    }

    public override void Push()
    {
        var byId = Context.Mic.Devices.ToDictionary(d => d.Id);

        // Hardware names ("Focusrite USB Audio") rather than endpoint names ("Analogue 1 + 2") —
        // on a narrow tile the hardware is what tells you which physical thing is involved.
        string[] devices = Context.Config.MuteDeviceIds
            .Select(id => byId.TryGetValue(id, out var d) ? d.Hardware : "(missing device)")
            .ToArray();

        // The tile can say whether the mic is switched on, but not whether anything is actually
        // listening. The privacy poll is the missing half.
        var use = Context.Privacy.Microphone;

        Post(new
        {
            muted = !Context.Mic.AnyLive,
            devices,
            apps = use.InUse ? use.Describe() : ""
        });
    }

    /// <summary>Notifications arrive on a COM thread; the UI and WebView2 are thread-affine.</summary>
    private void OnMicChanged() => Context.Dispatcher.BeginInvoke(Push);
}
```

`src/Deck.Shell/Widgets/CameraWidget.cs`:

```csharp
namespace Deck.Shell.Widgets;

/// <summary>An indicator only: there is no camera switch that could be trusted to have worked.</summary>
internal sealed class CameraWidget(WidgetContext context) : WidgetBase(context, "camera")
{
    public override void Start()
    {
        Context.Privacy.Changed += Push;
        Context.Privacy.Acquire();
    }

    public override void Stop()
    {
        Context.Privacy.Changed -= Push;
        Context.Privacy.Release();
    }

    public override void Push()
    {
        var camera = Context.Privacy.Camera;
        Post(new { inUse = camera.InUse, apps = camera.Describe() });
    }
}
```

- [ ] **Step 7: Write the timer widgets**

`src/Deck.Shell/Widgets/PomodoroWidget.cs`:

```csharp
using System.Media;
using Deck.Shell.Timers;

namespace Deck.Shell.Widgets;

internal sealed class PomodoroWidget(WidgetContext context) : WidgetBase(context, "pomodoro")
{
    private readonly PomodoroTimer _timer = new();

    public override void Start()
    {
        _timer.Changed += Push;
        _timer.Alert += OnAlert;
        Context.Tick.Ticked += OnTick;
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        _timer.Changed -= Push;
        _timer.Alert -= OnAlert;
        Context.Tick.Ticked -= OnTick;
        Context.Tick.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                _timer.Toggle();
                return true;

            case "reset":
                _timer.ResetSession();
                return true;

            default:
                return false;
        }
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "pomodoro") return false;

        _timer.Toggle();
        return true;
    }

    public override void Push() => Post(new
    {
        phase = _timer.Phase.ToString().ToLowerInvariant(),
        remaining = TimeFormat.Countdown(_timer.Remaining),
        blocks = _timer.CompletedBlocks,
        awaiting = _timer.AwaitingNextBlock
    });

    private void OnTick()
    {
        _timer.Tick();
        if (_timer.Phase != PomodoroPhase.Idle) Push();
    }

    private void OnAlert(string message)
    {
        // Played directly rather than leaning on the notification's own sound, which Focus
        // Assist and fullscreen games can suppress. A timer you don't hear is not a timer.
        SystemSounds.Exclamation.Play();
        Context.Notifier.Show("Pomodoro", message);
        Push();
    }
}
```

`src/Deck.Shell/Widgets/StopwatchWidget.cs`:

```csharp
using Deck.Shell.Timers;

namespace Deck.Shell.Widgets;

internal sealed class StopwatchWidget(WidgetContext context) : WidgetBase(context, "stopwatch")
{
    private readonly StopwatchTimer _watch = new();

    public override void Start()
    {
        _watch.Changed += Push;
        Context.Tick.Ticked += OnTick;
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        _watch.Changed -= Push;
        Context.Tick.Ticked -= OnTick;
        Context.Tick.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                _watch.Toggle();
                return true;

            case "reset":
                _watch.Reset();
                return true;

            default:
                return false;
        }
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "stopwatch") return false;

        _watch.Toggle();
        return true;
    }

    public override void Push() => Post(new
    {
        running = _watch.IsRunning,
        elapsed = TimeFormat.Clock(_watch.Elapsed),
        hasElapsed = _watch.HasElapsed
    });

    private void OnTick()
    {
        if (_watch.IsRunning) Push();
    }
}
```

- [ ] **Step 8: Write the preset and shortcut widgets**

`src/Deck.Shell/Widgets/PresetWidget.cs`:

```csharp
using Deck.Shell.Layout;
using Deck.Shell.Presets;

namespace Deck.Shell.Widgets;

/// <summary>One saved window layout. Deleting it is the main window's job, since that changes the config's list.</summary>
internal sealed class PresetWidget(WidgetContext context, string id) : WidgetBase(context, "preset", id)
{
    private string _state = "idle";
    private string? _summary;

    private Preset? Preset => Context.Config.Presets.FirstOrDefault(p => p.Id == Ref);

    public override bool Handle(string message)
    {
        if (message != "press") return false;

        _ = RunAsync();
        return true;
    }

    public override bool HandleHotkey(string action)
    {
        if (action != WidgetCatalog.PresetActionPrefix + Ref) return false;

        _ = RunAsync();
        return true;
    }

    public override void Push()
    {
        if (Preset is not { } preset) return;

        Post(new
        {
            name = preset.Name,
            count = preset.Entries.Count,
            state = _state,
            summary = _summary
        });
    }

    private async Task RunAsync()
    {
        if (Preset is not { } preset) return;

        _state = "running";
        Push();

        try
        {
            var report = await new PresetRunner().RunAsync(preset);
            _state = "done";
            _summary = report.Summary();
            Push();

            if (report.Failed.Count > 0)
            {
                Context.Notifier.Show($"{preset.Name}: {report.Failed.Count} didn't work",
                    string.Join("\n", report.Failed.Take(4)));
            }
        }
        catch (Exception ex)
        {
            _state = "error";
            _summary = ex.Message;
            Push();
        }
    }
}
```

`src/Deck.Shell/Widgets/ShortcutWidget.cs`:

```csharp
using System.Diagnostics;
using Deck.Shell.Config;

namespace Deck.Shell.Widgets;

internal sealed class ShortcutWidget(WidgetContext context, string id) : WidgetBase(context, "shortcut", id)
{
    private DeckShortcut? Shortcut => Context.Config.Shortcuts.FirstOrDefault(s => s.Id == Ref);

    public override bool Handle(string message)
    {
        if (message != "press") return false;

        Run();
        return true;
    }

    public override void Push()
    {
        if (Shortcut is not { } shortcut) return;

        Post(new { label = shortcut.Label, note = shortcut.Note ?? "" });
    }

    private void Run()
    {
        if (Shortcut is not { } shortcut) return;

        try
        {
            var info = new ProcessStartInfo(shortcut.FileName) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(shortcut.Arguments)) info.Arguments = shortcut.Arguments;

            Process.Start(info);
        }
        catch (Exception ex)
        {
            Context.Notifier.Show($"{shortcut.Label} didn't open", ex.Message);
        }
    }
}
```

- [ ] **Step 9: Build and test**

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)`.

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: `Passed!  - Failed: 0, Passed: 36`.

- [ ] **Step 10: Commit**

```bash
git add src/Deck.Shell/Widgets tests/Deck.Shell.Tests/TimeFormatTests.cs
git commit -m "Port the simple tiles into widget classes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Port the media, mixer and noise widgets, and the factory

**Files:**
- Create: `src/Deck.Shell/Widgets/NowPlayingWidget.cs`, `MixerWidget.cs`, `NoiseWidget.cs`, `WidgetFactory.cs`

**Interfaces:**
- Consumes: `MediaService` (Task 3), all Task 4 widgets.
- Produces:

| Class | Kind | Handles | Hotkey | Posts |
|---|---|---|---|---|
| `NowPlayingWidget` | nowplaying | `press`, `next` | `nowplaying` | `{hasSession, playing, title, artist, app}` |
| `MixerWidget` | mixer | `set:{json}`, `forget:<name>`, `commit`, `open` | — | `{apps, rows:[{name,label,icon,volume,muted,active,running}]}` |
| `NoiseWidget` | noise | `press`, `calibrate`, `threshold-set:<n>`, `threshold-commit` | `room` | full: `{armed, running, threshold, error, device}`; live: `{level}` |

  - `NoiseWidget.RestartCapture()` — public, called when the room sensor changes.
  - `static class WidgetFactory`: `IWidget? Create(WidgetPlacement placement, WidgetContext context)`

- [ ] **Step 1: Write the Now Playing widget**

`src/Deck.Shell/Widgets/NowPlayingWidget.cs`:

```csharp
using Deck.Shell.Media;

namespace Deck.Shell.Widgets;

internal sealed class NowPlayingWidget(WidgetContext context) : WidgetBase(context, "nowplaying")
{
    private NowPlaying Media => Context.Media.NowPlaying;

    public override void Start()
    {
        Context.Media.Refreshed += Push;
        Context.Media.Acquire();
    }

    public override void Stop()
    {
        Context.Media.Refreshed -= Push;
        Context.Media.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                _ = Media.TogglePlayPauseAsync();
                return true;

            case "next":
                _ = Media.SkipNextAsync();
                return true;

            default:
                return false;
        }
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "nowplaying") return false;

        _ = Media.TogglePlayPauseAsync();
        return true;
    }

    public override void Push() => Post(new
    {
        hasSession = Media.HasSession,
        playing = Media.IsPlaying,
        title = Media.Title,
        artist = Media.Artist,
        app = Media.App
    });
}
```

- [ ] **Step 2: Write the Mixer widget**

`src/Deck.Shell/Widgets/MixerWidget.cs`:

```csharp
using System.Text.Json;
using Deck.Shell.Apps;
using Deck.Shell.Audio;
using Deck.Shell.Config;

namespace Deck.Shell.Widgets;

internal sealed class MixerWidget(WidgetContext context) : WidgetBase(context, "mixer")
{
    private readonly HashSet<string> _audioAppsSeen = new(StringComparer.OrdinalIgnoreCase);
    private MixerWindow? _window;

    private VolumeMixer Mixer => Context.Media.Mixer;

    private DeckConfig Config => Context.Config;

    /// <summary>How many apps fit: the 2×2 tile has room for six, the 2×1 for three.</summary>
    private int RowLimit => Variant == "short" ? 3 : 6;

    public override void Start()
    {
        Context.Media.Refreshed += OnRefreshed;
        Context.Media.Acquire();
    }

    public override void Stop()
    {
        Context.Media.Refreshed -= OnRefreshed;
        Context.Media.Release();
        _window?.Close();
    }

    public override bool Handle(string message)
    {
        if (message.StartsWith("set:", StringComparison.Ordinal))
        {
            ApplyChange(message["set:".Length..]);
            return true;
        }

        if (message.StartsWith("forget:", StringComparison.Ordinal))
        {
            Forget(message["forget:".Length..]);
            return true;
        }

        switch (message)
        {
            case "commit":
                // Saved on release rather than on every pixel of a drag.
                Config.Save();
                return true;

            case "open":
                OpenWindow();
                return true;

            default:
                return false;
        }
    }

    public override void Push() => Post(new { apps = Mixer.Apps.Count, rows = BuildRows() });

    private void OnRefreshed()
    {
        RestoreRememberedLevels();
        Push();
    }

    /// <summary>
    /// Applies a remembered level when an app's audio session first appears — the Wave Link
    /// behaviour of a source keeping its level across restarts.
    ///
    /// Deliberately only on appearance, never continuously: re-asserting every tick would fight
    /// the user if they changed a volume anywhere else in Windows.
    /// </summary>
    private void RestoreRememberedLevels()
    {
        foreach (var app in Mixer.Apps)
        {
            // Remember where the app lives so its icon still resolves when it isn't running.
            if (app.Path is not null) Config.MixerAppPaths[app.Name] = app.Path;

            if (_audioAppsSeen.Contains(app.Name)) continue;

            if (Config.MixerLevels.TryGetValue(app.Name, out int level))
                Mixer.SetVolume(app.Name, level / 100f);
        }

        _audioAppsSeen.Clear();
        foreach (var app in Mixer.Apps) _audioAppsSeen.Add(app.Name);
    }

    /// <summary>
    /// The rows the tile shows. Remembered apps always appear, running or not, so a level can be
    /// set for something that isn't open yet; whatever else is making sound fills the rest.
    /// </summary>
    private object[] BuildRows()
    {
        var live = Mixer.Apps.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        string[] names = Config.MixerLevels.Keys
            .Concat(Mixer.Apps.Where(a => a.Active).Select(a => a.Name))
            .Concat(Mixer.Apps.Select(a => a.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(RowLimit)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return names.Select(object (name) =>
        {
            bool running = live.TryGetValue(name, out var app);
            string? path = running ? app!.Path : Config.MixerAppPaths.GetValueOrDefault(name);

            var identity = AppIdentityResolver.Resolve(path);

            return new
            {
                name,
                label = identity.DisplayName ?? name,
                icon = identity.IconDataUri,
                // A running app's real volume is the truth; a remembered one falls back to
                // whatever level was stored for its next launch.
                volume = running
                    ? (int)Math.Round(app!.Volume * 100)
                    : Config.MixerLevels.GetValueOrDefault(name, 100),
                muted = running && app!.Muted,
                active = running && app!.Active,
                running
            };
        }).ToArray();
    }

    /// <summary>
    /// Volume and mute arrive on the same message so a drag and a mute press can't race each
    /// other into two different refreshes.
    /// </summary>
    private void ApplyChange(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("name", out var nameElement)) return;
            string? name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name)) return;

            if (root.TryGetProperty("volume", out var volume) && volume.ValueKind == JsonValueKind.Number)
            {
                int level = Math.Clamp(volume.GetInt32(), 0, 100);

                // Remember it whether or not the app is running — that's the whole point.
                Config.MixerLevels[name] = level;
                Mixer.SetVolume(name, level / 100f);
            }

            if (root.TryGetProperty("muted", out var muted) &&
                muted.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                Mixer.SetMute(name, muted.GetBoolean());
                // Mute is a discrete press, so reflect it immediately rather than on the next tick.
                Mixer.Refresh();
                Push();
            }
        }
        catch (JsonException)
        {
            // Malformed message from the page; ignore.
        }
    }

    /// <summary>
    /// Drops a remembered app. Needed because a remembered app is shown whether or not it's
    /// running, so an uninstalled one would otherwise sit there forever.
    /// </summary>
    private void Forget(string name)
    {
        if (!Config.MixerLevels.Remove(name) & !Config.MixerAppPaths.Remove(name)) return;

        Config.Save();
        Mixer.Refresh();
        Push();
    }

    private void OpenWindow()
    {
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }

        _window = new MixerWindow(Mixer);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        _window.Activate();
    }
}
```

- [ ] **Step 3: Write the Noise widget**

`src/Deck.Shell/Widgets/NoiseWidget.cs`:

```csharp
using System.Globalization;
using System.Windows.Threading;
using Deck.Shell.Audio;

namespace Deck.Shell.Widgets;

/// <summary>
/// The room-noise monitor. Off the deck it closes its capture stream entirely — nothing listens
/// to the room while this tile is in the library.
/// </summary>
internal sealed class NoiseWidget(WidgetContext context) : WidgetBase(context, "noise")
{
    /// <summary>
    /// A disarmed noise monitor is supposed to be temporary, but this desktop can run for weeks
    /// without a restart, so "re-arms when the deck starts" would rarely fire. This gives that
    /// rule a heartbeat.
    /// </summary>
    private const int DailyRearmHour = 21;

    private const string ThresholdPrefix = "threshold-set:";

    private RoomMonitor? _room;
    private DispatcherTimer? _rearmTimer;
    private DateTime _lastRearm = DateTime.Now.Date.AddDays(-1);
    private string? _error;

    public override void Start()
    {
        _room = new RoomMonitor { Threshold = Context.Config.RoomThreshold, Armed = true };

        // Capture callbacks arrive on an audio thread; the UI and WebView2 are thread-affine.
        var dispatcher = Context.Dispatcher;
        _room.LevelChanged += level => dispatcher.BeginInvoke(() => PushLevel(level));
        _room.Failed += message => dispatcher.BeginInvoke(() =>
        {
            _error = message;
            Push();
        });
        _room.Breached += () => dispatcher.BeginInvoke(() =>
            Context.Notifier.Show("Keep it down 🤫", "The room is over your limit."));

        RestartCapture();

        _rearmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _rearmTimer.Tick += (_, _) => RearmIfDue();
        _rearmTimer.Start();
    }

    public override void Stop()
    {
        _rearmTimer?.Stop();
        _rearmTimer = null;
        _room?.Dispose();
        _room = null;
    }

    /// <summary>(Re)opens the capture stream on whichever device is currently the room sensor.</summary>
    public void RestartCapture()
    {
        if (_room is null) return;

        _room.Stop();
        _error = null;

        if (Context.Config.RoomSensorDeviceId is not { } id)
        {
            _error = "no room sensor selected";
            return;
        }

        var device = Context.Mic.Find(id);
        if (device is null)
        {
            _error = "room sensor not found";
            return;
        }

        _room.Start(device);
    }

    public override bool Handle(string message)
    {
        if (_room is null) return false;

        if (message.StartsWith(ThresholdPrefix, StringComparison.Ordinal))
        {
            SetThreshold(message[ThresholdPrefix.Length..]);
            return true;
        }

        switch (message)
        {
            case "press":
                ToggleArmed();
                return true;

            case "calibrate":
                Context.Config.RoomThreshold = _room.Calibrate();
                Context.Config.Save();
                Push();
                return true;

            case "threshold-commit":
                Context.Config.Save();
                Push();
                return true;

            default:
                return false;
        }
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "room" || _room is null) return false;

        ToggleArmed();
        return true;
    }

    public override void Push() => Post(new
    {
        armed = _room?.Armed ?? false,
        running = _room?.IsRunning ?? false,
        threshold = Math.Round(Context.Config.RoomThreshold),
        error = _error,
        device = DeviceName()
    });

    private void PushLevel(double level)
    {
        // A callback queued just before the tile was removed.
        if (_room is null) return;

        Post(new { level = Math.Round(level, 1) });
    }

    private string? DeviceName() =>
        Context.Config.RoomSensorDeviceId is { } id
            ? Context.Mic.Devices.FirstOrDefault(d => d.Id == id)?.Hardware
            : null;

    private void ToggleArmed()
    {
        if (_room is null) return;

        _room.Armed = !_room.Armed;
        Push();
    }

    /// <summary>
    /// Live threshold updates while the handle is being dragged: applied at once so the meter's
    /// over/under colouring tracks the mouse, but only written to disk on threshold-commit.
    /// </summary>
    private void SetThreshold(string raw)
    {
        if (_room is null) return;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return;

        value = Math.Clamp(value, 0, 100);
        _room.Threshold = value;
        Context.Config.RoomThreshold = value;
    }

    private void RearmIfDue()
    {
        var now = DateTime.Now;
        var todayAt = now.Date.AddHours(DailyRearmHour);

        if (now < todayAt || _lastRearm >= todayAt) return;

        _lastRearm = todayAt;
        if (_room is { Armed: false })
        {
            _room.Armed = true;
            Push();
        }
    }
}
```

- [ ] **Step 4: Write the factory**

`src/Deck.Shell/Widgets/WidgetFactory.cs`:

```csharp
using Deck.Shell.Layout;

namespace Deck.Shell.Widgets;

internal static class WidgetFactory
{
    public static IWidget? Create(WidgetPlacement placement, WidgetContext context) => placement switch
    {
        { Kind: "claude" } => new ClaudeWidget(context),
        { Kind: "weather" } => new WeatherWidget(context),
        { Kind: "nowplaying" } => new NowPlayingWidget(context),
        { Kind: "system" } => new SystemWidget(context),
        { Kind: "noise" } => new NoiseWidget(context),
        { Kind: "mic" } => new MicWidget(context),
        { Kind: "camera" } => new CameraWidget(context),
        { Kind: "clock" } => new ClockWidget(context),
        { Kind: "mixer" } => new MixerWidget(context),
        { Kind: "pomodoro" } => new PomodoroWidget(context),
        { Kind: "stopwatch" } => new StopwatchWidget(context),
        { Kind: "preset", Ref: { } id } => new PresetWidget(context, id),
        { Kind: "shortcut", Ref: { } id } => new ShortcutWidget(context, id),
        _ => null
    };
}
```

- [ ] **Step 5: Build and test**

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)`.

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: `Passed!  - Failed: 0, Passed: 36`.

- [ ] **Step 6: Commit**

```bash
git add src/Deck.Shell/Widgets
git commit -m "Port the media, mixer and noise tiles into widget classes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Drive the deck from the layout

Switches the app over: `MainWindow` hosts widgets, and the page builds its grid from the `layout` message. After this task the deck behaves exactly as today, except Pomodoro and Stopwatch are gone, the bottom-left two cells are empty, and presets are laid out as individual tiles. There is no edit mode yet.

**Files:**
- Rewrite: `src/Deck.Shell/MainWindow.xaml.cs`
- Modify: `src/Deck.Shell/HotkeyWindow.xaml.cs` (`Actions()` only)
- Modify: `src/Deck.Shell/Deck.Shell.csproj` (`Content` item)
- Rewrite: `src/Deck.Shell/ui/deck.html`
- Create: `src/Deck.Shell/ui/deck.css`, `src/Deck.Shell/ui/widgets.js`, `src/Deck.Shell/ui/deck.js`
- Create: `src/Deck.Shell/ui/dev/harness.html`, `src/Deck.Shell/ui/dev/harness.js`

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces (page globals later tasks use): in `deck.js` — `grid` (element), `layout` (last layout message), `cache`, `keyOf(kind, ref)`, `layoutOp(op)`, `senderFor(kind, ref)`, `render()`; in `widgets.js` — `Widgets` (map kind → `{click?, context?, deletable?, template(variant), bind?(tile, send, variant), update(tile, data, variant, send)}`), `q`, `setText`, `ICONS`, `horizontalDrag`. `click`/`context` may be a string or a function of the variant.
- Produces (host): `MainWindow.PushLayout()`, `CommitLayout(DeckLayout)`, `HandleLayoutOp(string json)` (only `delete` in this task), `DeleteItem(string kind, string? reference)`.

- [ ] **Step 1: Rewrite MainWindow**

Replace the whole of `src/Deck.Shell/MainWindow.xaml.cs` with:

```csharp
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Deck.Shell.Audio;
using Deck.Shell.Config;
using Deck.Shell.Hotkeys;
using Deck.Shell.Interop;
using Deck.Shell.Layout;
using Deck.Shell.Notifications;
using Deck.Shell.Startup;
using Deck.Shell.Widgets;
using Microsoft.Web.WebView2.Core;
using static Deck.Shell.Interop.NativeMethods;

namespace Deck.Shell;

public partial class MainWindow : Window
{
    /// <summary>Physical pixels of screen height the deck claims along the bottom edge.</summary>
    private const int DeckHeightPx = 520;

    /// <summary>
    /// The page is served from the ui folder under this made-up host name rather than injected
    /// as one string, so its stylesheet and scripts can live in their own files. ".example" is
    /// reserved and never resolves, so nothing leaves the machine.
    /// </summary>
    private const string UiHost = "deck.example";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private AppBarHost? _appBar;
    private ForegroundTracker? _tracker;
    private MicController? _mic;
    private Notifier? _notifier;
    private HotkeyManager? _hotkeys;
    private HotkeyWindow? _hotkeyWindow;
    private DeviceWindow? _deviceWindow;
    private MediaService? _media;
    private WidgetHost? _host;
    private DeckConfig _config = new();

    /// <summary>Edit mode lives here rather than in the page, because the tray can switch it on.</summary>
    private bool _editing;

    private IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += (_, _) => Cleanup();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // The whole product thesis in three flags: clicking the deck must not change which
        // window has keyboard focus, and the deck must never appear in Alt-Tab.
        long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        // Global hotkeys register against this window, which works fine despite it never
        // taking focus — that's exactly why they're worth having.
        _hotkeys = new HotkeyManager(_hwnd);
        _hotkeys.Triggered += RunAction;

        _tracker = new ForegroundTracker();

        _appBar = new AppBarHost(_hwnd);
        App.ReleaseScreenSpace = () => _appBar?.Remove();

        if (_appBar.Register())
        {
            _appBar.Dock(Monitors.PickDeckMonitor(), DeckHeightPx);
        }
        else
        {
            MessageBox.Show("Could not register the AppBar; the deck will float instead.", "Deck");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeys is not null && _hotkeys.HandleMessage(msg, wParam))
        {
            handled = true;
            return IntPtr.Zero;
        }

        // Refusing activation on click is what keeps the user's app in front. WS_EX_NOACTIVATE
        // alone is not enough once child HWNDs (WebView2's) are in the picture.
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        if (_appBar is not null && msg == unchecked((int)_appBar.CallbackMessage))
        {
            if (wParam.ToInt32() == ABN_POSCHANGED) _appBar.ReapplyPosition();
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _config = DeckConfig.Load();
        if (LayoutMigration.Prepare(_config)) _config.Save();

        _notifier = new Notifier();
        _notifier.ExitRequested += Close;

        // Turn autostart on once, then leave the decision to the tray toggle — re-enabling it
        // on every launch would silently override the user switching it off.
        if (!_config.AutoStartInitialised)
        {
            AutoStart.Set(true);
            _config.AutoStartInitialised = true;
            _config.Save();
        }
        else
        {
            AutoStart.RefreshIfEnabled();
        }

        _notifier.AddItem("Microphones…", OpenDevices);
        _notifier.AddItem("Shortcuts…", OpenHotkeys);
        _notifier.AddToggle("Start with Windows", AutoStart.IsEnabled, AutoStart.Set);
        _notifier.AddExitItem();

        ApplyHotkeys();
        StartAudio();
        StartWidgets();

        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Deck", "WebView2");
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(env);

            var settings = Web.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;

            Web.CoreWebView2.WebMessageReceived += OnWebMessage;
            // The page can only be told the state once its listener exists.
            Web.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                PushLayout();
                _host?.PushAll();
            };

            // Served files can be cached across runs; after an update the deck must never run
            // yesterday's scripts against today's host.
            await Web.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);

            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                UiHost, Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Deny);
            Web.CoreWebView2.Navigate($"https://{UiHost}/deck.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "WebView2 failed to start");
        }
    }

    /// <summary>
    /// The microphone controller lives outside the widgets: the mic tile, the noise tile and the
    /// Microphones window all read the same devices.
    /// </summary>
    private void StartAudio()
    {
        _mic = new MicController();
        var devices = _mic.Refresh();

        if (_config.IsFirstRun || _config.MuteDeviceIds.Count == 0)
        {
            _config.ApplyDefaults(devices);
            _config.Save();
        }

        _mic.Select(_config.MuteDeviceIds);
    }

    private void StartWidgets()
    {
        var tick = new TickService();
        _media = new MediaService();

        var context = new WidgetContext
        {
            Config = _config,
            Notifier = _notifier!,
            Mic = _mic!,
            Tick = tick,
            Media = _media,
            Privacy = new PrivacyService(tick),
            Dispatcher = Dispatcher,
            Post = PostWidget
        };

        _host = new WidgetHost(
            placement => WidgetFactory.Create(placement, context),
            (kind, reference) => PostWidget(kind, reference, new { failed = true }));

        _host.Sync(_config.Layout);
    }

    private void PostWidget(string kind, string? reference, object data) =>
        PostJson(new { type = "widget", kind, @ref = reference, data });

    private void PostJson(object message)
    {
        if (Web.CoreWebView2 is null) return;
        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    /// <summary>
    /// Everything the page needs to draw the grid: where each widget sits and how big it is,
    /// whether edit mode is on, and what the library panel can offer.
    /// </summary>
    private void PushLayout()
    {
        var layout = new DeckLayout(_config.Layout);

        PostJson(new
        {
            type = "layout",
            editing = _editing,
            columns = DeckLayout.Columns,
            rows = DeckLayout.Rows,
            placements = _config.Layout.Select(p =>
            {
                var size = WidgetCatalog.Find(p.Kind, p.Variant)!;
                return new
                {
                    kind = p.Kind,
                    variant = p.Variant,
                    @ref = p.Ref,
                    col = p.Col,
                    row = p.Row,
                    w = size.Width,
                    h = size.Height
                };
            }),
            library = BuildLibrary(layout)
        });
    }

    /// <summary>
    /// What the library offers: every built-in widget not on the deck, in each of its sizes,
    /// then the presets and shortcuts that aren't on it.
    /// </summary>
    private IEnumerable<object> BuildLibrary(DeckLayout layout)
    {
        foreach (var kind in layout.UnplacedBuiltIns())
            yield return LibraryItem(kind, null, kind.Title);

        var preset = WidgetCatalog.Find("preset")!;
        foreach (var p in _config.Presets.Where(p => !layout.IsPlaced("preset", p.Id)))
            yield return LibraryItem(preset, p.Id, p.Name);

        var shortcut = WidgetCatalog.Find("shortcut")!;
        foreach (var s in _config.Shortcuts.Where(s => !layout.IsPlaced("shortcut", s.Id)))
            yield return LibraryItem(shortcut, s.Id, s.Label);
    }

    private static object LibraryItem(WidgetKind kind, string? reference, string title) => new
    {
        kind = kind.Id,
        @ref = reference,
        title,
        group = kind.PerItem ? kind.Title : null,
        variants = kind.Variants.Select(v => new { variant = v.Id, label = v.Label, w = v.Width, h = v.Height })
    };

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        if (message.StartsWith("widget:", StringComparison.Ordinal))
            RouteWidgetMessage(message["widget:".Length..]);
        else if (message.StartsWith("layout:", StringComparison.Ordinal))
            HandleLayoutOp(message["layout:".Length..]);
    }

    private void RouteWidgetMessage(string json)
    {
        WidgetMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<WidgetMessage>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;   // Malformed message from the page; ignore.
        }

        if (message is { Kind: { } kind, Msg: { } msg }) _host?.Route(kind, message.Ref, msg);
    }

    private void HandleLayoutOp(string json)
    {
        LayoutOp? op;
        try
        {
            op = JsonSerializer.Deserialize<LayoutOp>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (op is { Op: "delete", Kind: { } kind }) DeleteItem(kind, op.Ref);
    }

    /// <summary>
    /// Permanently deletes a preset or shortcut, from a right-click on its tile or its library
    /// card. MessageBox rather than an in-deck confirm: a dialog inside a non-activating window
    /// can't reliably take the keyboard, and deleting should be deliberate.
    /// </summary>
    private void DeleteItem(string kind, string? reference)
    {
        if (reference is null) return;

        if (kind == "preset" && _config.Presets.FirstOrDefault(p => p.Id == reference) is { } preset)
        {
            var answer = MessageBox.Show(
                $"Delete the preset \"{preset.Name}\"?\n\nThis only removes the button. Nothing on your screen changes.",
                "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            _config.Presets.Remove(preset);
            _config.Hotkeys.RemoveAll(h => h.Action == WidgetCatalog.PresetActionPrefix + reference);
            ApplyHotkeys(notifyOnConflict: false);
        }
        else if (kind == "shortcut" && _config.Shortcuts.FirstOrDefault(s => s.Id == reference) is { } shortcut)
        {
            var answer = MessageBox.Show(
                $"Remove the \"{shortcut.Label}\" shortcut?",
                "Remove shortcut", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            _config.Shortcuts.Remove(shortcut);
        }
        else
        {
            return;
        }

        var layout = new DeckLayout(_config.Layout);
        layout.Remove(kind, reference);
        CommitLayout(layout);
    }

    /// <summary>Saves a changed layout, starts and stops widgets to match, and redraws the page.</summary>
    private void CommitLayout(DeckLayout layout)
    {
        _config.Layout = layout.Placements.ToList();
        _config.Save();
        _host?.Sync(_config.Layout);
        PushLayout();
    }

    private void ApplyHotkeys(bool notifyOnConflict = true)
    {
        _hotkeys?.Apply(_config.Hotkeys);

        if (notifyOnConflict && _hotkeys is { Conflicts.Count: > 0 })
            _notifier?.Show("Some shortcuts couldn't be registered", string.Join("\n", _hotkeys.Conflicts));
    }

    /// <summary>
    /// Fired by a global hotkey; window messages already arrive on the UI thread. Only widgets
    /// on the deck can answer, so a hotkey for a widget in the library does nothing.
    /// </summary>
    private void RunAction(string action) => _host?.Hotkey(action);

    private void ApplyDeviceConfig()
    {
        _mic?.Select(_config.MuteDeviceIds);
        _host?.Find<NoiseWidget>()?.RestartCapture();
        _host?.PushAll();
    }

    private void OpenDevices()
    {
        if (_deviceWindow is { IsVisible: true })
        {
            _deviceWindow.Activate();
            return;
        }

        _deviceWindow = new DeviceWindow(_config, _mic?.Devices ?? []);
        _deviceWindow.Changed += ApplyDeviceConfig;
        _deviceWindow.Closed += (_, _) => _deviceWindow = null;
        _deviceWindow.Show();
        _deviceWindow.Activate();
    }

    private void OpenHotkeys()
    {
        if (_hotkeyWindow is { IsVisible: true })
        {
            _hotkeyWindow.Activate();
            return;
        }

        // Registered hotkeys swallow their own combos before any window sees them, so
        // re-binding Ctrl+Alt+M would be impossible while Ctrl+Alt+M is still live.
        _hotkeys?.UnregisterAll();

        _hotkeyWindow = new HotkeyWindow(_config);

        _hotkeyWindow.Changed += () =>
        {
            // Register briefly to find out whether Windows will accept the combo, report that,
            // then stand down again so the next recording isn't intercepted.
            ApplyHotkeys(notifyOnConflict: false);
            string[] conflicts = _hotkeys?.Conflicts.ToArray() ?? [];
            _hotkeys?.UnregisterAll();
            _hotkeyWindow?.SetConflicts(conflicts);
        };

        _hotkeyWindow.Closed += (_, _) =>
        {
            _hotkeyWindow = null;
            ApplyHotkeys();
        };

        _hotkeyWindow.Show();
        _hotkeyWindow.Activate();
    }

    private void Cleanup()
    {
        App.ReleaseScreenSpace = null;

        // Widgets first: the noise tile holds a capture stream on a device the mic controller
        // owns, so it has to let go before the controller is disposed.
        _host?.StopAll();
        _media?.Dispose();
        _mic?.Dispose();

        _hotkeys?.Dispose();
        _notifier?.Dispose();
        _tracker?.Dispose();
        _appBar?.Remove();
    }

    private sealed record WidgetMessage(string? Kind, string? Ref, string? Msg);

    private sealed record LayoutOp(string? Op, string? Kind, string? Variant, string? Ref, int Col, int Row);
}
```

- [ ] **Step 2: Name presets by id in the Shortcuts window**

In `src/Deck.Shell/HotkeyWindow.xaml.cs`, replace the preset loop at the end of `Actions()`:

```csharp
        for (int i = 0; i < _config.Presets.Count; i++)
            yield return ($"preset:{i}", $"Run preset · {_config.Presets[i].Name}");
```

with:

```csharp
        foreach (var preset in _config.Presets)
            yield return ($"{Deck.Shell.Layout.WidgetCatalog.PresetActionPrefix}{preset.Id}", $"Run preset · {preset.Name}");
```

- [ ] **Step 3: Keep the dev harness out of the published app**

In `src/Deck.Shell/Deck.Shell.csproj`, replace:

```xml
    <Content Include="ui\**\*" CopyToOutputDirectory="PreserveNewest" />
```

with:

```xml
    <!-- ui\dev is a browser harness for working on the page; the app never loads it. -->
    <Content Include="ui\**\*" Exclude="ui\dev\**" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)`. (`MainWindow` no longer references `PomodoroTimer`, `ClaudeWatcher` and the rest directly; those classes stay, used by the widgets.)

- [ ] **Step 5: Replace the page shell**

Replace the whole of `src/Deck.Shell/ui/deck.html` with (no doctype, as before — keeps the rendering mode the tiles were designed in):

```html
<meta charset="utf-8" />
<title>Deck</title>
<link rel="stylesheet" href="deck.css" />
<script defer src="widgets.js"></script>
<script defer src="deck.js"></script>
```

- [ ] **Step 6: Write the stylesheet**

`src/Deck.Shell/ui/deck.css` — the old inline styles, re-keyed from `#id` to `.tile.w-<kind>` classes (tiles are no longer unique elements), with the fixed grid positions removed:

```css
:root {
  --bg: #0b0d10;
  --tile: #171b21;
  --tile-hi: #1f252d;
  --line: #2a313b;
  --text: #e6eaf0;
  --dim: #8b95a4;
  --ok: #3fb950;
  --ok-bg: #12251a;
  --ok-line: #2c5138;
  --warn: #d29922;
  --danger: #f85149;
  --danger-bg: #3d1418;
  --danger-line: #7d2a2f;
  --preset: #1b2530;
  --preset-line: #2f4256;
  --preset-text: #7cc4ff;
}

* { box-sizing: border-box; }

html, body {
  margin: 0;
  height: 100%;
  background: var(--bg);
  color: var(--text);
  font: 13px/1.35 "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
  user-select: none;
  -webkit-user-drag: none;
  overflow: hidden;
  cursor: default;
}

body { display: flex; flex-direction: column; }

#stage { position: relative; flex: 1; min-height: 0; }

/* One 6x3 grid. Where each tile sits comes from the saved layout, not from this file. */
#grid {
  height: 100%;
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  grid-template-rows: repeat(3, minmax(0, 1fr));
  gap: 10px;
  padding: 12px;
}

.tile {
  position: relative;
  background: var(--tile);
  border: 1px solid var(--line);
  border-radius: 13px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 5px;
  text-align: center;
  padding: 10px;
  overflow: hidden;
  transition: background 90ms ease, transform 90ms ease, border-color 120ms ease;
}

.tile.pressable:hover { background: var(--tile-hi); }
.tile.pressable:active { transform: scale(0.97); }

.tile .label { font-size: 15px; font-weight: 650; letter-spacing: 0.03em; }
.tile .sub { font-size: 12px; color: var(--dim); }

/* Which hardware a tile is bound to. Configuration rather than state, so it sits quietly
   at the bottom of its own tile instead of in a shared status bar. */
.tile .device {
  max-width: 100%;
  font-size: 11px;
  color: #6d7787;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.tile.blank { background: transparent; border: 1px dashed #1a1f26; }

.tile svg { width: 30px; height: 30px; }

.tile.failed { border-color: var(--danger-line); }
.tile.failed .label { color: var(--danger); }

/* --- now playing --- */
.w-nowplaying .np-title {
  font-size: 13px;
  font-weight: 600;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.w-nowplaying.playing .np-title { color: var(--ok); }
.tile.w-nowplaying.idle { color: var(--dim); }

/* The record turns only while something is actually playing — a spinning disc over a paused
   track would be the tile lying about its state, same rule as everywhere else. */
.w-nowplaying .np-record {
  width: 34px;
  height: 34px;
  color: var(--dim);
  animation: spin 3.4s linear infinite;
  animation-play-state: paused;
}
.w-nowplaying.playing .np-record { color: var(--ok); animation-play-state: running; }
.w-nowplaying.idle .np-record { opacity: 0.35; }

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* --- mixer --- */
.tile.w-mixer { gap: 2px; padding: 8px 10px; justify-content: center; }
.w-mixer .mx-rows { width: 100%; display: flex; flex-direction: column; gap: 3px; }

.mx-row {
  display: grid;
  grid-template-columns: 22px 82px 1fr 26px;
  gap: 8px;
  align-items: center;
  font-size: 11.5px;
}

/* The icon replaces a name column that could never fit one — and doubles as the mute
   button, since there is no room for a separate control. */
.mx-icon {
  width: 20px;
  height: 20px;
  display: block;
  cursor: pointer;
  image-rendering: -webkit-optimize-contrast;
}

.mx-name {
  color: var(--text);
  font-weight: 600;
  font-size: 10px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  cursor: pointer;
  text-align: left;
}

/* Padding, not height: the visible bar stays slim while the grab area is finger-sized. */
.mx-barwrap { padding: 7px 0; cursor: ew-resize; touch-action: none; }
.mx-bar {
  display: block;
  height: 9px;
  border-radius: 5px;
  background: #0e1116;
  border: 1px solid var(--line);
  overflow: hidden;
}
.mx-fill { display: block; height: 100%; background: var(--preset-text); }

.mx-val { text-align: right; font-variant-numeric: tabular-nums; color: var(--dim); }

.mx-row.quiet .mx-fill { opacity: 0.45; }
.mx-row.quiet .mx-name { color: var(--dim); }
.mx-row.quiet .mx-icon { opacity: 0.7; }

/* Remembered but not running: the level is still editable, it just isn't applied yet. */
.mx-row.offline .mx-name { color: #5b6474; font-style: italic; }
.mx-row.offline .mx-fill { opacity: 0.28; }
.mx-row.offline .mx-val { color: #5b6474; }
.mx-row.offline .mx-icon { opacity: 0.4; filter: grayscale(1); }

.mx-row.muted .mx-icon { opacity: 0.45; filter: grayscale(1); }
.mx-row.muted .mx-name { color: var(--danger); text-decoration: line-through; }
.mx-row.muted .mx-fill { background: var(--danger); opacity: 0.4; }

/* --- claude sessions --- */
.w-claude .cc-count { font-size: 26px; font-weight: 650; font-variant-numeric: tabular-nums; }
.tile.w-claude.idle { color: var(--dim); }
.w-claude.idle .cc-count { color: var(--dim); }

/* Working is informational; waiting needs you, so it gets the louder colour. */
.tile.w-claude.working { background: var(--preset); border-color: var(--preset-line); }
.w-claude.working .label, .w-claude.working .cc-count { color: var(--preset-text); }

/* Only rendered when alerts are off — silence is the exception worth stating. */
.w-claude .cc-muted { color: var(--warn); font-weight: 600; }

.tile.w-claude.waiting { background: #33270e; border-color: #6b5312; }
.w-claude.waiting .label, .w-claude.waiting .cc-count, .w-claude.waiting .sub { color: var(--warn); }

/* --- weather --- */

/* Icon and temperature share a line rather than stacking. That buys back a whole row, which
   is what was making this tile feel squeezed. */
.tile.w-weather { gap: 7px; padding: 12px 14px; }
.w-weather .wx-now { display: flex; align-items: center; justify-content: center; gap: 10px; }
.w-weather .wx-icon {
  font-family: "Segoe UI Emoji", "Apple Color Emoji", system-ui;
  font-size: 26px;
  line-height: 1;
}
.w-weather .wx-temp {
  font-size: 30px;
  font-weight: 650;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}
.w-weather.stale { opacity: 0.5; }

/* Three labelled figures rather than one cramped line of "feels 25° · H32° L19°". */
.wx-stats {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 6px;
  width: 100%;
  margin-top: 3px;
  padding-top: 9px;
  border-top: 1px solid var(--line);
}
.wx-stats > div { display: flex; flex-direction: column; align-items: center; gap: 3px; }
.wx-k { font-size: 8.5px; letter-spacing: 0.09em; font-weight: 700; color: var(--dim); }
.wx-v { font-size: 14px; font-weight: 650; font-variant-numeric: tabular-nums; }

/* --- presets --- */
.tile.w-preset { background: var(--preset); border-color: var(--preset-line); }
.tile.w-preset:hover { background: #223041; }
.w-preset .label { color: var(--preset-text); }
.tile.w-preset.busy { border-color: var(--warn); }
.w-preset.busy .sub { color: var(--warn); }

/* Shortcuts sit beside presets but read as a quieter kind of button — they only launch
   something, they don't rearrange your desk. */
.tile.w-shortcut { background: #1a1f27; border-color: #333c48; }
.tile.w-shortcut:hover { background: #222834; }
.w-shortcut .label { color: #b9c4d2; }

/* --- world clock --- */
.tile.w-clock { padding: 10px 12px; }
.w-clock .wc-rows { width: 100%; display: flex; flex-direction: column; gap: 5px; }

/* Three columns, not two: the day marker gets its own cell so every time starts at the
   same x and the column reads as a column. */
.wc-row {
  display: grid;
  grid-template-columns: 1fr 14px auto;
  gap: 6px;
  align-items: baseline;
}
.wc-city {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.03em;
  color: var(--dim);
  text-align: left;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.wc-time { font-size: 14px; font-weight: 650; font-variant-numeric: tabular-nums; }
.wc-day { font-size: 9.5px; color: var(--warn); text-align: right; }

.wc-row.local .wc-city { color: var(--text); }
.wc-row.local .wc-time { color: var(--preset-text); }

/* --- mic --- */
/* Both states carry a full tile colour, so the difference is readable from across the desk
   rather than depending on the colour of one word. */
.w-mic .icon-muted { display: none; }
.tile.w-mic { background: var(--ok-bg); border-color: var(--ok-line); }
.tile.w-mic:hover { background: #17301f; }
.w-mic .label, .w-mic .icon-live { color: var(--ok); }

.tile.w-mic.muted { background: var(--danger-bg); border-color: var(--danger-line); }
.tile.w-mic.muted:hover { background: #4a181d; }
.w-mic.muted .label, .w-mic.muted .icon-muted { color: var(--danger); }
.w-mic.muted .icon-live { display: none; }
.w-mic.muted .icon-muted { display: block; }

/* --- noise --- */
.meter-wrap { width: 100%; padding: 8px 0; cursor: ew-resize; touch-action: none; }
.meter { position: relative; width: 100%; height: 14px; }
.meter .track {
  height: 100%;
  border-radius: 7px;
  background: #0e1116;
  border: 1px solid var(--line);
  overflow: hidden;
}
.meter .fill {
  height: 100%;
  width: 0%;
  background: var(--ok);
  transition: width 60ms linear, background 120ms ease;
}
.meter .tick {
  position: absolute;
  top: -4px;
  bottom: -4px;
  width: 3px;
  border-radius: 2px;
  background: var(--text);
  transform: translateX(-50%);
  box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.65);
  pointer-events: none;
}
.meter .tick::after {
  content: '';
  position: absolute;
  left: 50%;
  top: -6px;
  transform: translateX(-50%);
  border-left: 5px solid transparent;
  border-right: 5px solid transparent;
  border-top: 6px solid var(--text);
}
.w-noise.dragging .meter .tick { background: var(--warn); }
.w-noise.dragging .meter .tick::after { border-top-color: var(--warn); }
.w-noise.over .meter .fill { background: var(--danger); }

/* Over the limit with alerts armed: fast and unmissable, because this is the state that
   needs you to act. Disarmed and over stays quiet — that difference is the point. */
.tile.w-noise.alerting { animation: noiseAlert 0.15s ease-in-out infinite; }
.w-noise.alerting .label { color: var(--danger); }

@keyframes noiseAlert {
  0%, 100% { background: var(--tile); border-color: var(--line); }
  50% { background: var(--danger-bg); border-color: var(--danger); }
}
.w-noise .reading { font-size: 20px; font-weight: 650; font-variant-numeric: tabular-nums; }
.w-noise .reading .unit { font-size: 11px; color: var(--dim); font-weight: 400; }
.tile.w-noise.disarmed { opacity: 0.55; }
.w-noise.disarmed .sub { color: var(--warn); }
.w-noise.dead .reading { color: var(--danger); }

/* --- camera: indicator only, so deliberately not .pressable ---
   Same colour language as the mic: red with a slash means nothing is capturing you, green
   means something is. Matching them means one glance answers both questions. */
.tile.w-camera { background: var(--danger-bg); border-color: var(--danger-line); color: var(--danger); }
.w-camera .icon-live { display: none; }
.w-camera .label, .w-camera .sub { color: var(--danger); }

.tile.w-camera.live {
  background: var(--ok-bg);
  border-color: var(--ok-line);
  color: var(--ok);
  /* Slow and shallow: a reminder, not an alarm. */
  animation: cameraBreathe 3.4s ease-in-out infinite;
}
.w-camera.live .icon-live { display: block; }
.w-camera.live .icon-off { display: none; }
.w-camera.live .label, .w-camera.live .sub { color: var(--ok); }

@keyframes cameraBreathe {
  0%, 100% { box-shadow: inset 0 0 0 0 rgba(63, 185, 80, 0); }
  50% { box-shadow: inset 0 0 22px 0 rgba(63, 185, 80, 0.22); }
}

/* --- system stats: read-only, so no .pressable --- */
/* Three bars and nothing else, so they get the room the detail lines used to take. */
.tile.w-system { gap: 12px; padding: 12px 14px; justify-content: center; }
.w-system .stat {
  display: grid;
  grid-template-columns: 32px 1fr 36px;
  gap: 8px;
  align-items: center;
  width: 100%;
  font-size: 12.5px;
}
.w-system .k { color: var(--dim); font-weight: 600; letter-spacing: 0.04em; }
.w-system .v { text-align: right; font-variant-numeric: tabular-nums; }
.w-system .bar {
  height: 10px;
  border-radius: 4px;
  background: #0e1116;
  border: 1px solid var(--line);
  overflow: hidden;
}
.w-system .fill {
  display: block;
  height: 100%;
  width: 0%;
  background: var(--ok);
  transition: width 240ms linear, background 200ms ease;
}
.w-system .fill.mid { background: var(--warn); }
.w-system .fill.high { background: var(--danger); }

/* --- timers --- */
.w-pomodoro .time, .w-stopwatch .time {
  font-size: 23px;
  font-weight: 650;
  font-variant-numeric: tabular-nums;
}
.tile.w-pomodoro.work { border-color: var(--preset-line); }
.w-pomodoro.work .label, .w-pomodoro.work .time { color: var(--preset-text); }
.tile.w-pomodoro.break { background: var(--ok-bg); border-color: var(--ok-line); }
.w-pomodoro.break .label, .w-pomodoro.break .time { color: var(--ok); }
.tile.w-pomodoro.awaiting { border-color: var(--warn); }
.w-pomodoro.awaiting .sub { color: var(--warn); }
.w-stopwatch.running .time { color: var(--ok); }
```

- [ ] **Step 7: Write the widget renderers**

`src/Deck.Shell/ui/widgets.js` (no comments — organisation rule):

```js
const q = (tile, selector) => tile.querySelector(selector);
const setText = (tile, selector, value) => { q(tile, selector).textContent = value ?? ''; };
const clampPercent = (value) => Math.max(0, Math.min(100, Math.round(value)));

const ICONS = {
  record: `<svg class="np-record" viewBox="0 0 24 24">
      <circle cx="12" cy="12" r="11.2" fill="#0e1116" stroke="#333c48" stroke-width="0.8" />
      <circle cx="12" cy="12" r="8.6" fill="none" stroke="#2a313b" stroke-width="0.6" />
      <circle cx="12" cy="12" r="6.9" fill="none" stroke="#2a313b" stroke-width="0.6" />
      <circle cx="12" cy="12" r="5.2" fill="none" stroke="#2a313b" stroke-width="0.6" />
      <circle cx="12" cy="12" r="3.4" fill="currentColor" />
      <circle cx="12" cy="12" r="0.9" fill="#0b0d10" />
      <path d="M12 1.2a10.8 10.8 0 0 1 7.6 3.2" fill="none" stroke="#4a5666" stroke-width="0.9" stroke-linecap="round" />
    </svg>`,
  micLive: `<svg class="icon-live" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      <rect x="9" y="2" width="6" height="12" rx="3" />
      <path d="M5 11a7 7 0 0 0 14 0" />
      <line x1="12" y1="18" x2="12" y2="22" />
    </svg>`,
  micMuted: `<svg class="icon-muted" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      <rect x="9" y="2" width="6" height="12" rx="3" />
      <path d="M5 11a7 7 0 0 0 14 0" />
      <line x1="12" y1="18" x2="12" y2="22" />
      <line x1="3" y1="3" x2="21" y2="21" />
    </svg>`,
  cameraLive: `<svg class="icon-live" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      <rect x="2" y="6" width="14" height="12" rx="2.5" />
      <path d="M16 10.5 22 7v10l-6-3.5z" />
    </svg>`,
  cameraOff: `<svg class="icon-off" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      <rect x="2" y="6" width="14" height="12" rx="2.5" />
      <path d="M16 10.5 22 7v10l-6-3.5z" />
      <line x1="3" y1="3" x2="21" y2="21" />
    </svg>`
};

function horizontalDrag(handle, measure, onValue, onEnd) {
  let active = false;
  const valueAt = (x) => {
    const box = measure().getBoundingClientRect();
    return clampPercent(((x - box.left) / box.width) * 100);
  };

  handle.addEventListener('pointerdown', (e) => {
    e.preventDefault();
    e.stopPropagation();
    active = true;
    handle.setPointerCapture(e.pointerId);
    onValue(valueAt(e.clientX));
  });
  handle.addEventListener('pointermove', (e) => {
    if (active) onValue(valueAt(e.clientX));
  });

  const end = (e) => {
    if (!active) return;
    active = false;
    try { handle.releasePointerCapture(e.pointerId); } catch (_) {}
    onEnd();
  };
  handle.addEventListener('pointerup', end);
  handle.addEventListener('pointercancel', end);
  handle.addEventListener('click', (e) => e.stopPropagation());

  return { get active() { return active; } };
}

function setStat(tile, name, value) {
  const fill = q(tile, '.f-' + name);
  fill.style.width = value + '%';
  fill.classList.toggle('mid', value >= 60 && value < 85);
  fill.classList.toggle('high', value >= 85);
  setText(tile, '.v-' + name, value + '%');
}

function renderNoise(tile) {
  const s = tile.noise;
  tile.classList.toggle('disarmed', !s.armed);
  tile.classList.toggle('dead', !s.running);
  q(tile, '.tick').style.left = s.threshold + '%';
  setText(tile, '.room-state', !s.running
    ? (s.error || 'not listening')
    : (s.armed ? 'armed · limit ' + s.threshold : 'ALERTS OFF · limit ' + s.threshold));
}

function renderMixer(tile, rows, total, send) {
  const host = q(tile, '.mx-rows');
  host.innerHTML = '';

  setText(tile, '.mx-empty',
    rows.length === 0 ? 'no audio apps'
    : total > rows.length ? (total - rows.length) + ' more · right-click'
    : '');

  for (const r of rows) {
    const row = document.createElement('div');
    row.className = 'mx-row'
      + (r.muted ? ' muted' : '')
      + (r.active ? '' : ' quiet')
      + (r.running === false ? ' offline' : '');

    const icon = document.createElement(r.icon ? 'img' : 'span');
    icon.className = 'mx-icon';
    if (r.icon) {
      icon.src = r.icon;
      icon.alt = '';
      icon.draggable = false;
    }

    const name = document.createElement('span');
    name.className = 'mx-name';
    name.textContent = r.label || r.name;

    const tip = (r.running === false
      ? r.name + ' — not running; this level applies when it next opens'
      : r.name + (r.muted ? ' — muted, click to unmute' : ' — click to mute'))
      + '\nRight-click to remove it from the mixer';
    icon.title = tip;
    name.title = tip;

    row.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      e.stopPropagation();
      send('forget:' + r.name);
    });

    if (r.running !== false) {
      const toggleMute = () => send('set:' + JSON.stringify({ name: r.name, muted: !r.muted }));
      icon.addEventListener('click', toggleMute);
      name.addEventListener('click', toggleMute);
    }

    const wrap = document.createElement('span');
    wrap.className = 'mx-barwrap';
    const bar = document.createElement('span');
    bar.className = 'mx-bar';
    const fill = document.createElement('span');
    fill.className = 'mx-fill';
    fill.style.width = r.volume + '%';
    bar.append(fill);
    wrap.append(bar);

    const value = document.createElement('span');
    value.className = 'mx-val';
    value.textContent = r.volume;

    horizontalDrag(wrap, () => bar, (v) => {
      tile.mixerDragging = true;
      fill.style.width = v + '%';
      value.textContent = v;
      send('set:' + JSON.stringify({ name: r.name, volume: v }));
    }, () => {
      tile.mixerDragging = false;
      send('commit');
    });

    row.append(icon, name, wrap, value);
    host.append(row);
  }
}

const Widgets = {
  claude: {
    click: 'press',
    context: 'notify-toggle',
    template: () => `
      <div class="label">CLAUDE</div>
      <div class="cc-count">–</div>
      <div class="sub cc-state">no sessions</div>
      <div class="device cc-names"></div>
      <div class="device cc-muted cc-alerts"></div>`,
    update(tile, d) {
      const total = d.waiting + d.working;
      tile.classList.toggle('waiting', d.waiting > 0);
      tile.classList.toggle('working', d.waiting === 0 && d.working > 0);
      tile.classList.toggle('idle', total === 0);
      setText(tile, '.cc-count', total === 0 ? '–' : (d.waiting > 0 ? d.waiting : d.working));
      setText(tile, '.cc-state',
        total === 0 ? 'no sessions'
        : d.waiting > 0 ? 'waiting on you' + (d.working ? ' · ' + d.working + ' working' : '')
        : 'working');
      setText(tile, '.cc-names', d.names);
      setText(tile, '.cc-alerts', d.notify ? '' : 'alerts muted');
      tile.title = 'Click to jump to a session · right-click to '
        + (d.notify ? 'mute' : 'unmute') + ' notifications';
    }
  },

  weather: {
    template: () => `
      <div class="label">ANKARA</div>
      <div class="wx-now"><span class="wx-icon">–</span><span class="wx-temp">–</span></div>
      <div class="sub wx-label">loading…</div>
      <div class="wx-stats">
        <div><span class="wx-k">FEELS</span><span class="wx-v wx-feels">–</span></div>
        <div><span class="wx-k">HIGH</span><span class="wx-v wx-high">–</span></div>
        <div><span class="wx-k">LOW</span><span class="wx-v wx-low">–</span></div>
      </div>`,
    update(tile, d) {
      tile.classList.toggle('stale', d.stale);
      setText(tile, '.wx-icon', d.icon);
      setText(tile, '.wx-temp', d.temp);
      setText(tile, '.wx-label', d.error || d.label);
      setText(tile, '.wx-feels', d.available ? d.feels : '–');
      setText(tile, '.wx-high', d.available ? d.high : '–');
      setText(tile, '.wx-low', d.available ? d.low : '–');
    }
  },

  nowplaying: {
    click: 'press',
    context: 'next',
    template: () => `
      ${ICONS.record}
      <div class="np-title">nothing</div>
      <div class="sub np-artist"></div>
      <div class="device np-app"></div>`,
    update(tile, d) {
      tile.classList.toggle('playing', d.playing);
      tile.classList.toggle('idle', !d.hasSession);
      setText(tile, '.np-title', d.hasSession ? (d.title || 'untitled') : 'nothing playing');
      setText(tile, '.np-artist', !d.hasSession ? '' : d.playing ? (d.artist || 'playing') : 'paused');
      setText(tile, '.np-app', d.app);
    }
  },

  system: {
    template: () => ['cpu', 'ram', 'gpu'].map((k) => `
      <div class="stat">
        <span class="k">${k.toUpperCase()}</span>
        <span class="bar"><span class="fill f-${k}"></span></span>
        <span class="v v-${k}">–</span>
      </div>`).join(''),
    update(tile, d) {
      setStat(tile, 'cpu', d.cpu);
      setStat(tile, 'ram', d.ram);
      if (d.gpuAvailable) {
        setStat(tile, 'gpu', d.gpu);
      } else {
        q(tile, '.f-gpu').style.width = '0%';
        setText(tile, '.v-gpu', 'n/a');
      }
    }
  },

  noise: {
    click: 'press',
    context: 'calibrate',
    template: () => `
      <div class="label">NOISE</div>
      <div class="meter-wrap">
        <div class="meter">
          <div class="track"><div class="fill"></div></div>
          <div class="tick"></div>
        </div>
      </div>
      <div class="reading"><span class="room-level">0</span><span class="unit">/100</span></div>
      <div class="sub room-state">starting…</div>
      <div class="device room-device">—</div>`,
    bind(tile, send) {
      const state = { armed: true, running: false, error: null, threshold: 60 };
      tile.noise = state;
      state.drag = horizontalDrag(q(tile, '.meter-wrap'), () => q(tile, '.meter'), (v) => {
        tile.classList.add('dragging');
        state.threshold = v;
        renderNoise(tile);
        send('threshold-set:' + v);
      }, () => {
        tile.classList.remove('dragging');
        send('threshold-commit');
      });
    },
    update(tile, d) {
      const state = tile.noise;
      if ('armed' in d) {
        state.armed = d.armed;
        state.running = d.running;
        state.error = d.error;
        if (!state.drag.active) state.threshold = d.threshold;
        setText(tile, '.room-device', d.device || 'no sensor selected');
        renderNoise(tile);
      }
      if ('level' in d) {
        q(tile, '.meter .fill').style.width = d.level + '%';
        setText(tile, '.room-level', Math.round(d.level));
        const over = d.level > state.threshold;
        tile.classList.toggle('over', over);
        tile.classList.toggle('alerting', over && state.armed);
      }
    }
  },

  mic: {
    click: 'press',
    template: () => `
      ${ICONS.micLive}${ICONS.micMuted}
      <div class="label mute-label">MIC LIVE</div>
      <div class="sub mute-sub"></div>
      <div class="device mute-device">—</div>`,
    update(tile, d) {
      tile.classList.toggle('muted', d.muted);
      setText(tile, '.mute-label', d.muted ? 'MUTED' : 'MIC LIVE');
      setText(tile, '.mute-device', d.devices.length ? d.devices.join(' · ') : 'no device selected');
      setText(tile, '.mute-sub', d.apps);
    }
  },

  camera: {
    template: () => `
      ${ICONS.cameraLive}${ICONS.cameraOff}
      <div class="label">CAMERA</div>
      <div class="sub camera-state">off</div>`,
    update(tile, d) {
      tile.classList.toggle('live', d.inUse);
      setText(tile, '.camera-state', d.inUse ? (d.apps || 'in use') : 'off');
    }
  },

  clock: {
    template: () => `<div class="wc-rows"></div>`,
    update(tile, d) {
      const host = q(tile, '.wc-rows');
      host.innerHTML = '';
      for (const c of d.cities) {
        const row = document.createElement('div');
        row.className = 'wc-row' + (c.local ? ' local' : '');
        row.innerHTML = '<span class="wc-city"></span><span class="wc-day"></span><span class="wc-time"></span>';
        row.children[0].textContent = c.label;
        row.children[1].textContent = c.day === 0 ? '' : (c.day > 0 ? '+' + c.day : String(c.day));
        row.children[2].textContent = c.time;
        host.append(row);
      }
    }
  },

  mixer: {
    context: 'open',
    template: () => `<div class="mx-rows"></div><div class="device mx-empty">no audio apps</div>`,
    update(tile, d, variant, send) {
      if (!tile.mixerDragging) renderMixer(tile, d.rows, d.apps, send);
    }
  },

  pomodoro: {
    click: 'press',
    context: 'reset',
    template: () => `<div class="label">POMODORO</div><div class="time">25:00</div><div class="sub">ready</div>`,
    update(tile, p) {
      tile.classList.toggle('work', p.phase === 'work');
      tile.classList.toggle('break', p.phase === 'break');
      tile.classList.toggle('awaiting', p.awaiting);
      const blocks = p.blocks ? ` · ${p.blocks} done` : '';
      setText(tile, '.time', p.phase === 'idle' ? '25:00' : p.remaining);
      setText(tile, '.sub',
        p.phase === 'work' ? 'work' + blocks
        : p.phase === 'break' ? 'break — step away' + blocks
        : p.awaiting ? 'break over · click to resume' + blocks
        : 'ready' + blocks);
    }
  },

  stopwatch: {
    click: 'press',
    context: 'reset',
    template: () => `<div class="label">STOPWATCH</div><div class="time">00:00</div><div class="sub">click to start</div>`,
    update(tile, w) {
      tile.classList.toggle('running', w.running);
      setText(tile, '.time', w.elapsed);
      setText(tile, '.sub', w.running ? 'running' : (w.hasElapsed ? 'stopped · right-click to reset' : 'click to start'));
    }
  },

  preset: {
    click: 'press',
    deletable: true,
    template: () => `<div class="label"></div><div class="sub"></div>`,
    update(tile, d) {
      tile.classList.toggle('busy', d.state === 'running');
      setText(tile, '.label', d.name);
      setText(tile, '.sub', d.state === 'running' ? 'working…'
        : (d.summary || `${d.count} window${d.count === 1 ? '' : 's'}`));
    }
  },

  shortcut: {
    click: 'press',
    deletable: true,
    template: () => `<div class="label"></div><div class="sub"></div>`,
    update(tile, d) {
      setText(tile, '.label', d.label);
      setText(tile, '.sub', d.note);
    }
  }
};
```

- [ ] **Step 8: Write the page core**

`src/Deck.Shell/ui/deck.js` (no comments):

```js
document.body.innerHTML = '<div id="stage"><main id="grid"></main></div>';

const bridge = window.chrome.webview;
const post = (message) => bridge.postMessage(message);
const grid = document.getElementById('grid');
const cache = new Map();
let layout = { columns: 6, rows: 3, editing: false, placements: [], library: [] };

const keyOf = (kind, ref) => kind + '|' + (ref ?? '');
const layoutOp = (op) => post('layout:' + JSON.stringify(op));
const senderFor = (kind, ref) => (msg) => post('widget:' + JSON.stringify({ kind, ref: ref ?? null, msg }));
const pick = (value, variant) => (typeof value === 'function' ? value(variant) : value);

function buildTile(p) {
  const def = Widgets[p.kind];
  const click = pick(def.click, p.variant);
  const context = pick(def.context, p.variant);
  const send = senderFor(p.kind, p.ref);

  const tile = document.createElement('div');
  tile.className = `tile w-${p.kind} v-${p.variant}` + (click ? ' pressable' : '');
  tile.style.gridColumn = `${p.col + 1} / span ${p.w}`;
  tile.style.gridRow = `${p.row + 1} / span ${p.h}`;
  tile.dataset.key = keyOf(p.kind, p.ref);
  tile.innerHTML = def.template(p.variant);
  if (def.bind) def.bind(tile, send, p.variant);

  tile.addEventListener('click', () => {
    if (!layout.editing && click) send(click);
  });
  tile.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    if (layout.editing) return;
    if (def.deletable) layoutOp({ op: 'delete', kind: p.kind, ref: p.ref });
    else if (context) send(context);
  });

  return tile;
}

function buildEmpty(col, row) {
  const cell = document.createElement('div');
  cell.className = 'tile blank';
  cell.style.gridColumn = String(col + 1);
  cell.style.gridRow = String(row + 1);
  return cell;
}

function applyData(tile, p, data) {
  if (data.failed) {
    tile.classList.add('failed');
    tile.classList.remove('pressable');
    tile.innerHTML = '<div class="label"></div><div class="sub">failed to start</div>';
    setText(tile, '.label', p.kind.toUpperCase());
    return;
  }
  Widgets[p.kind].update(tile, data, p.variant, senderFor(p.kind, p.ref));
}

function render() {
  grid.innerHTML = '';
  const taken = new Set();

  for (const p of layout.placements) {
    if (!Widgets[p.kind]) continue;
    for (let c = p.col; c < p.col + p.w; c++) {
      for (let r = p.row; r < p.row + p.h; r++) taken.add(c + ',' + r);
    }
    const tile = buildTile(p);
    grid.append(tile);
    const cached = cache.get(tile.dataset.key);
    if (cached) applyData(tile, p, cached);
  }

  for (let r = 0; r < layout.rows; r++) {
    for (let c = 0; c < layout.columns; c++) {
      if (taken.has(c + ',' + r)) continue;
      grid.append(buildEmpty(c, r));
    }
  }
}

function onLayout(message) {
  layout = message;
  const live = new Set(layout.placements.map((p) => keyOf(p.kind, p.ref)));
  for (const key of [...cache.keys()]) {
    if (!live.has(key)) cache.delete(key);
  }
  render();
}

function onWidget(message) {
  const key = keyOf(message.kind, message.ref);
  cache.set(key, { ...(cache.get(key) || {}), ...message.data });

  const p = layout.placements.find((x) => keyOf(x.kind, x.ref) === key);
  if (!p) return;
  const tile = grid.querySelector(`[data-key="${CSS.escape(key)}"]`);
  if (tile) applyData(tile, p, message.data);
}

bridge.addEventListener('message', (ev) => {
  const m = ev.data;
  if (m.type === 'layout') onLayout(m);
  else if (m.type === 'widget') onWidget(m);
});
```

- [ ] **Step 9: Write the dev harness**

`src/Deck.Shell/ui/dev/harness.html`:

```html
<meta charset="utf-8" />
<title>Deck harness</title>
<link rel="stylesheet" href="../deck.css" />
<script src="harness.js"></script>
<script defer src="../widgets.js"></script>
<script defer src="../deck.js"></script>
```

`src/Deck.Shell/ui/dev/harness.js` (no comments) — a fake host: it answers layout ops with the same rules as `DeckLayout` and feeds sample data, so the page can be exercised in a browser:

```js
(() => {
  const SIZES = {
    claude: { standard: [1, 1] },
    weather: { standard: [1, 1], compact: [1, 1], hourly: [2, 1] },
    nowplaying: { standard: [1, 1], wide: [2, 1] },
    system: { standard: [1, 1] },
    noise: { standard: [1, 1] },
    mic: { standard: [1, 1] },
    camera: { standard: [1, 1] },
    clock: { standard: [1, 1] },
    mixer: { tall: [2, 2], short: [2, 1] },
    pomodoro: { standard: [1, 1] },
    stopwatch: { standard: [1, 1] },
    preset: { standard: [1, 1] },
    shortcut: { standard: [1, 1] }
  };
  const TITLES = {
    claude: 'Claude', weather: 'Weather', nowplaying: 'Now Playing', system: 'System', noise: 'Noise',
    mic: 'Mic', camera: 'Camera', clock: 'World Clock', mixer: 'Mixer', pomodoro: 'Pomodoro', stopwatch: 'Stopwatch'
  };
  const LABELS = {
    standard: '1×1', compact: '1×1 compact', hourly: '2×1 · next hours',
    wide: '2×1 · art & controls', tall: '2×2 · 6 apps', short: '2×1 · 3 apps'
  };
  const ART = 'data:image/svg+xml;utf8,' + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="#2b4a6b"/><circle cx="5" cy="5" r="3" fill="#7cc4ff"/></svg>');

  const SAMPLE = {
    claude: { waiting: 2, working: 0, names: 'Process queue · PMI Case Study', notify: false },
    weather: {
      available: true, icon: '☁️', label: 'Overcast', temp: '19°', feels: '17°', high: '20°', low: '14°', stale: false, error: null,
      hourly: [['15', '☁️', '19°'], ['16', '⛅', '18°'], ['17', '⛅', '17°'], ['18', '🌤️', '16°'], ['19', '☀️', '15°'], ['20', '☀️', '14°']]
        .map(([hour, icon, temp]) => ({ hour, icon, temp }))
    },
    nowplaying: { hasSession: true, playing: true, title: 'Teardrop', artist: 'Massive Attack', app: 'Spotify', art: ART },
    system: { cpu: 47, ram: 66, gpuAvailable: true, gpu: 22 },
    noise: { armed: true, running: false, threshold: 60, error: 'room sensor not found', device: null, level: 0 },
    mic: { muted: false, devices: ['Focusrite USB Audio'], apps: 'super-squad' },
    camera: { inUse: true, apps: 'super-squad' },
    clock: {
      cities: [
        { label: 'London', time: '13:35', day: 0, local: false },
        { label: 'Berlin', time: '14:35', day: 0, local: false },
        { label: 'Ankara', time: '15:35', day: 0, local: true },
        { label: 'Melbourne', time: '22:35', day: 0, local: false }
      ]
    },
    mixer: {
      apps: 6,
      rows: [
        { name: 'brave', label: 'Brave Browser', icon: null, volume: 85, muted: false, active: false, running: false },
        { name: 'chrome', label: 'Google Chrome', icon: null, volume: 81, muted: false, active: true, running: true },
        { name: 'Discord', label: 'Discord', icon: null, volume: 78, muted: false, active: false, running: false },
        { name: 'Gather', label: 'Gather', icon: null, volume: 95, muted: false, active: false, running: false },
        { name: 'Spotify', label: 'Spotify', icon: null, volume: 53, muted: false, active: false, running: false },
        { name: 'System', label: 'Volume Mixer', icon: null, volume: 19, muted: false, active: false, running: true }
      ]
    },
    pomodoro: { phase: 'idle', remaining: '25:00', blocks: 0, awaiting: false },
    stopwatch: { running: false, elapsed: '00:00', hasElapsed: false }
  };

  const presets = [{ id: 'p1', name: 'WORK', count: 5 }, { id: 'p2', name: 'GAMING', count: 2 }];
  let editing = false;
  let placements = [
    ['preset', 'standard', 'p1', 0, 0], ['claude', 'standard', null, 4, 0], ['weather', 'standard', null, 5, 0],
    ['nowplaying', 'standard', null, 0, 1], ['system', 'standard', null, 1, 1], ['noise', 'standard', null, 2, 1],
    ['mic', 'standard', null, 3, 1], ['mixer', 'tall', null, 4, 1], ['clock', 'standard', null, 2, 2],
    ['camera', 'standard', null, 3, 2]
  ].map(([kind, variant, ref, col, row]) => ({ kind, variant, ref, col, row }));

  const listeners = [];
  const log = [];
  const emit = (data) => listeners.forEach((fn) => fn({ data }));
  const size = (p) => SIZES[p.kind][p.variant];
  const same = (p, kind, ref) => p.kind === kind && (p.ref ?? null) === (ref ?? null);
  const covers = (p, c, r) => {
    const [w, h] = size(p);
    return c >= p.col && c < p.col + w && r >= p.row && r < p.row + h;
  };
  const at = (c, r) => placements.find((p) => covers(p, c, r));
  const fits = (kind, variant, col, row, ignore) => {
    const dims = SIZES[kind] && SIZES[kind][variant];
    if (!dims) return false;
    const [w, h] = dims;
    if (col < 0 || row < 0 || col + w > 6 || row + h > 3) return false;
    for (let c = col; c < col + w; c++) {
      for (let r = row; r < row + h; r++) {
        const other = at(c, r);
        if (other && other !== ignore) return false;
      }
    }
    return true;
  };

  function sendLayout() {
    const placed = (kind, ref) => placements.some((p) => same(p, kind, ref));
    const builtIns = Object.keys(TITLES).filter((k) => !placed(k, null)).map((k) => ({
      kind: k, ref: null, title: TITLES[k], group: null,
      variants: Object.entries(SIZES[k]).map(([variant, [w, h]]) => ({ variant, label: LABELS[variant], w, h }))
    }));
    const items = presets.filter((p) => !placed('preset', p.id)).map((p) => ({
      kind: 'preset', ref: p.id, title: p.name, group: 'Preset',
      variants: [{ variant: 'standard', label: '1×1', w: 1, h: 1 }]
    }));
    emit({
      type: 'layout', editing, columns: 6, rows: 3,
      placements: placements.map((p) => { const [w, h] = size(p); return { ...p, w, h }; }),
      library: builtIns.concat(items)
    });
  }

  function sendData() {
    for (const [kind, data] of Object.entries(SAMPLE)) emit({ type: 'widget', kind, ref: null, data });
    for (const p of presets) {
      emit({ type: 'widget', kind: 'preset', ref: p.id, data: { name: p.name, count: p.count, state: 'idle', summary: null } });
    }
  }

  function handleLayout(op) {
    const found = placements.find((p) => same(p, op.kind, op.ref));
    if (op.op === 'edit') editing = true;
    else if (op.op === 'done') editing = false;
    else if (op.op === 'remove') placements = placements.filter((p) => p !== found);
    else if (op.op === 'place' && !found && fits(op.kind, op.variant, op.col, op.row, null)) {
      placements.push({ kind: op.kind, variant: op.variant, ref: op.ref ?? null, col: op.col, row: op.row });
      sendData();
    } else if (op.op === 'move' && found && !(found.col === op.col && found.row === op.row)) {
      if (fits(found.kind, found.variant, op.col, op.row, found)) {
        found.col = op.col;
        found.row = op.row;
      } else {
        const other = at(op.col, op.row);
        if (other && other !== found && String(size(other)) === String(size(found))) {
          const { col, row } = found;
          found.col = other.col;
          found.row = other.row;
          other.col = col;
          other.row = row;
        }
      }
    } else if (op.op === 'new-preset') {
      const id = 'p' + (presets.length + 1);
      presets.push({ id, name: 'NEW', count: 3 });
      if (fits('preset', 'standard', op.col, op.row, null)) {
        placements.push({ kind: 'preset', variant: 'standard', ref: id, col: op.col, row: op.row });
      }
      sendData();
    } else if (op.op === 'delete') {
      const index = presets.findIndex((p) => p.id === op.ref);
      if (index >= 0) presets.splice(index, 1);
      placements = placements.filter((p) => !same(p, op.kind, op.ref));
    }
    sendLayout();
  }

  Object.defineProperty(window, 'chrome', {
    configurable: true,
    value: {
      webview: {
        addEventListener: (type, fn) => listeners.push(fn),
        postMessage: (message) => {
          log.push(message);
          if (message.startsWith('layout:')) handleLayout(JSON.parse(message.slice('layout:'.length)));
        }
      }
    }
  });

  window.harness = { log, emit, sendLayout, sendData, placements: () => placements };

  document.addEventListener('DOMContentLoaded', () => {
    sendData();
    sendLayout();
  });
})();
```

- [ ] **Step 10: Check the page in the browser**

Open the harness in the built-in browser at `file:///C:/Users/justb/Workspace/Personal/deck/src/Deck.Shell/ui/dev/harness.html` (`mcp__Claude_Browser__preview_start` with that `url`). If the pane refuses `file://`, serve the folder instead: add a `.claude/launch.json` configuration `{"name": "deck-ui", "runtimeExecutable": "npx", "runtimeArgs": ["--yes", "http-server", "src/Deck.Shell/ui", "-p", "5178", "-c-1"], "port": 5178}` and open `http://localhost:5178/dev/harness.html`.

Resize to the deck's shape (`resize_window` width 1077, height 519) and take a screenshot. Expected:
- Top row: WORK preset tile at column 1 and three empty dashed cells, then CLAUDE (amber "2 waiting on you", "alerts muted") and ANKARA weather.
- Middle row: Now Playing ("Teardrop"), System bars (47/66/22%), NOISE ("room sensor not found"), MIC LIVE (green), then the 2×2 mixer with six rows spanning rows 2–3.
- Bottom row: **two empty dashed cells** (Pomodoro and Stopwatch are gone), World Clock, CAMERA (green).
- The same visual styling as the deck today (see the user's screenshot: dark tiles, coloured mic/camera/claude states).
- `read_console_messages` with `onlyErrors: true` returns nothing.

Also run in the page (`javascript_tool`): `document.querySelector('.w-mic').click(); harness.log.at(-1)` → expected `'widget:{"kind":"mic","ref":null,"msg":"press"}'`.

- [ ] **Step 11: Build and test**

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q` → `Build succeeded.`
Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Passed!  - Failed: 0, Passed: 36`.
Check that `src/Deck.Shell/bin/Debug/net10.0-windows10.0.19041.0/ui/` contains `deck.html`, `deck.css`, `widgets.js`, `deck.js` and **no** `dev` folder.

- [ ] **Step 12: Commit**

```bash
git add src/Deck.Shell
git commit -m "Build the deck from the saved layout through widget classes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Edit mode and the library panel

**Files:**
- Modify: `src/Deck.Shell/MainWindow.xaml.cs` (`HandleLayoutOp`, new `OpenCapture`, new `EnterEditMode`, tray item)
- Create: `src/Deck.Shell/ui/edit.js`
- Modify: `src/Deck.Shell/ui/deck.js` (`render()`), `src/Deck.Shell/ui/deck.html`, `src/Deck.Shell/ui/dev/harness.html`, `src/Deck.Shell/ui/deck.css` (append)

**Interfaces:**
- Consumes: `DeckLayout.Place/Remove/Move` (Task 1); `CommitLayout`, `PushLayout`, `DeleteItem`, `LayoutOp` record (Task 6); page globals `grid`, `layout`, `keyOf`, `layoutOp` (Task 6).
- Produces: `Edit` global in `edit.js` with `decorateTile(tile, placement)`, `decorateEmpty(cell, col, row)`, `afterRender()`. Host ops: `edit`, `done`, `place`, `remove`, `move`, `new-preset`, `delete`.

- [ ] **Step 1: Handle every layout op in the host**

In `src/Deck.Shell/MainWindow.xaml.cs`, replace the whole `HandleLayoutOp` method with:

```csharp
    private void HandleLayoutOp(string json)
    {
        LayoutOp? op;
        try
        {
            op = JsonSerializer.Deserialize<LayoutOp>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (op?.Op is null) return;

        var layout = new DeckLayout(_config.Layout);
        bool changed = false;

        switch (op.Op)
        {
            case "edit":
                _editing = true;
                break;

            case "done":
                _editing = false;
                break;

            case "place" when op.Kind is not null:
                string variant = op.Variant ?? WidgetCatalog.Find(op.Kind)?.Default.Id ?? "";
                changed = layout.Place(op.Kind, variant, op.Ref, op.Col, op.Row);
                break;

            case "remove" when op.Kind is not null:
                changed = layout.Remove(op.Kind, op.Ref);
                break;

            case "move" when op.Kind is not null:
                changed = layout.Move(op.Kind, op.Ref, op.Col, op.Row);
                break;

            case "new-preset":
                OpenCapture(op.Col, op.Row);
                return;

            case "delete" when op.Kind is not null:
                DeleteItem(op.Kind, op.Ref);
                return;
        }

        // Pushed even when nothing changed, so a refused drag snaps back to where it was.
        if (changed) CommitLayout(layout);
        else PushLayout();
    }

    private void EnterEditMode()
    {
        _editing = true;
        PushLayout();
    }

    /// <summary>
    /// Capture runs in its own ordinary window: the deck can never take keyboard focus, and
    /// naming a preset and typing URLs both need a keyboard. The new preset lands in the cell
    /// the library was opened from; if that cell has filled up meanwhile, it waits in the library.
    /// </summary>
    private void OpenCapture(int col, int row)
    {
        var capture = new CaptureWindow();
        capture.Saved += preset =>
        {
            _config.Presets.Add(preset);
            var layout = new DeckLayout(_config.Layout);
            layout.Place("preset", WidgetCatalog.Standard, preset.Id, col, row);
            CommitLayout(layout);
        };
        capture.Show();
        capture.Activate();
    }
```

In `OnLoaded`, add the tray item first, so the menu reads Edit layout / Microphones… / Shortcuts…:

```csharp
        _notifier.AddItem("Edit layout", EnterEditMode);
        _notifier.AddItem("Microphones…", OpenDevices);
```

(replacing the single existing `_notifier.AddItem("Microphones…", OpenDevices);` line).

- [ ] **Step 2: Build**

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 3: Hook edit mode into rendering**

In `src/Deck.Shell/ui/deck.js`, replace the `render` function with:

```js
function render() {
  grid.innerHTML = '';
  const taken = new Set();

  for (const p of layout.placements) {
    if (!Widgets[p.kind]) continue;
    for (let c = p.col; c < p.col + p.w; c++) {
      for (let r = p.row; r < p.row + p.h; r++) taken.add(c + ',' + r);
    }
    const tile = buildTile(p);
    grid.append(tile);
    const cached = cache.get(tile.dataset.key);
    if (cached) applyData(tile, p, cached);
    Edit.decorateTile(tile, p);
  }

  for (let r = 0; r < layout.rows; r++) {
    for (let c = 0; c < layout.columns; c++) {
      if (taken.has(c + ',' + r)) continue;
      const cell = buildEmpty(c, r);
      Edit.decorateEmpty(cell, c, r);
      grid.append(cell);
    }
  }

  Edit.afterRender();
}
```

A failure message arriving live replaces the tile's contents, which drops its edit badges. (During `render` that can't happen twice: `render` decorates after `applyData`.) In `onWidget`, replace:

```js
  if (tile) applyData(tile, p, message.data);
```

with:

```js
  if (!tile) return;
  applyData(tile, p, message.data);
  if (message.data.failed) Edit.decorateTile(tile, p);
```

Add `<script defer src="edit.js"></script>` as the last line of `src/Deck.Shell/ui/deck.html`, and `<script defer src="../edit.js"></script>` as the last line of `src/Deck.Shell/ui/dev/harness.html`.

- [ ] **Step 4: Write edit mode**

`src/Deck.Shell/ui/edit.js` (no comments):

```js
const Edit = (() => {
  const stage = document.getElementById('stage');

  const library = document.createElement('section');
  library.id = 'library';
  library.hidden = true;
  stage.append(library);

  const drop = document.createElement('div');
  drop.id = 'drop';
  drop.hidden = true;

  const bar = document.createElement('footer');
  bar.id = 'editbar';
  bar.innerHTML = '<span>Drag a tile to move it · × sends it to the library · + adds a widget</span><button class="done">Done</button>';
  bar.querySelector('.done').addEventListener('click', () => layoutOp({ op: 'done' }));
  document.body.append(bar);

  let libraryCell = null;

  const sameWidget = (a, b) => keyOf(a.kind, a.ref) === keyOf(b.kind, b.ref);

  function occupied(except) {
    const cells = new Map();
    for (const p of layout.placements) {
      if (except && sameWidget(p, except)) continue;
      for (let c = p.col; c < p.col + p.w; c++) {
        for (let r = p.row; r < p.row + p.h; r++) cells.set(c + ',' + r, p);
      }
    }
    return cells;
  }

  function fits(w, h, col, row, except) {
    if (col < 0 || row < 0 || col + w > layout.columns || row + h > layout.rows) return false;
    const cells = occupied(except);
    for (let c = col; c < col + w; c++) {
      for (let r = row; r < row + h; r++) {
        if (cells.has(c + ',' + r)) return false;
      }
    }
    return true;
  }

  function dropOutcome(p, col, row) {
    if (col === p.col && row === p.row) return 'none';
    if (fits(p.w, p.h, col, row, p)) return 'move';
    const other = occupied(p).get(col + ',' + row);
    return other && other.w === p.w && other.h === p.h ? 'swap' : 'bad';
  }

  function cellAt(x, y) {
    const box = grid.getBoundingClientRect();
    const style = getComputedStyle(grid);
    const padX = parseFloat(style.paddingLeft);
    const padY = parseFloat(style.paddingTop);
    const gapX = parseFloat(style.columnGap) || 0;
    const gapY = parseFloat(style.rowGap) || 0;
    const cellW = (box.width - 2 * padX - (layout.columns - 1) * gapX) / layout.columns;
    const cellH = (box.height - 2 * padY - (layout.rows - 1) * gapY) / layout.rows;
    return {
      col: Math.floor((x - box.left - padX + gapX / 2) / (cellW + gapX)),
      row: Math.floor((y - box.top - padY + gapY / 2) / (cellH + gapY))
    };
  }

  function showDrop(p, target, outcome) {
    const inside = target.col >= 0 && target.row >= 0
      && target.col + p.w <= layout.columns && target.row + p.h <= layout.rows;
    if (outcome === 'none' || !inside) {
      drop.hidden = true;
      return;
    }
    drop.hidden = false;
    drop.className = outcome === 'bad' ? 'bad' : 'ok';
    drop.style.gridColumn = `${target.col + 1} / span ${p.w}`;
    drop.style.gridRow = `${target.row + 1} / span ${p.h}`;
  }

  function startDrag(e, tile, p, handle) {
    e.preventDefault();
    const origin = cellAt(e.clientX, e.clientY);
    const grab = { col: origin.col - p.col, row: origin.row - p.row };
    const start = { x: e.clientX, y: e.clientY };
    let target = null;

    handle.setPointerCapture(e.pointerId);
    tile.classList.add('lifted');

    const move = (ev) => {
      tile.style.transform = `translate(${ev.clientX - start.x}px, ${ev.clientY - start.y}px)`;
      const at = cellAt(ev.clientX, ev.clientY);
      target = { col: at.col - grab.col, row: at.row - grab.row };
      showDrop(p, target, dropOutcome(p, target.col, target.row));
    };

    const end = (ev) => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', end);
      handle.removeEventListener('pointercancel', end);
      try { handle.releasePointerCapture(ev.pointerId); } catch (_) {}
      tile.classList.remove('lifted');
      tile.style.transform = '';
      drop.hidden = true;

      const outcome = target ? dropOutcome(p, target.col, target.row) : 'none';
      if (ev.type === 'pointerup' && (outcome === 'move' || outcome === 'swap')) {
        layoutOp({ op: 'move', kind: p.kind, ref: p.ref, col: target.col, row: target.row });
      }
    };

    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', end);
    handle.addEventListener('pointercancel', end);
  }

  function decorateTile(tile, p) {
    if (!layout.editing) return;

    const shield = document.createElement('div');
    shield.className = 'shield';
    shield.addEventListener('pointerdown', (e) => startDrag(e, tile, p, shield));

    const remove = document.createElement('button');
    remove.className = 'remove';
    remove.textContent = '×';
    remove.title = 'Send to the library';
    remove.addEventListener('pointerdown', (e) => e.stopPropagation());
    remove.addEventListener('click', (e) => {
      e.stopPropagation();
      layoutOp({ op: 'remove', kind: p.kind, ref: p.ref });
    });

    tile.append(shield, remove);
  }

  function decorateEmpty(cell, col, row) {
    if (layout.editing) {
      cell.classList.add('add');
      cell.innerHTML = '<div class="label">+</div>';
      cell.title = 'Add a widget here';
      cell.addEventListener('click', () => {
        libraryCell = { col, row };
        renderLibrary();
      });
    } else {
      cell.title = 'Click to edit the deck';
      cell.addEventListener('click', () => layoutOp({ op: 'edit' }));
    }
  }

  function closeLibrary() {
    libraryCell = null;
    library.hidden = true;
  }

  function needs(w, h) {
    return w === 2 && h === 2 ? 'needs a 2×2 space here' : 'needs ' + (w * h) + ' free cells here';
  }

  function card(title, detail, disabled, onPick, onDelete) {
    const el = document.createElement('div');
    el.className = 'card' + (disabled ? ' disabled' : '');
    el.innerHTML = '<div class="card-title"></div><div class="card-detail"></div>';
    el.querySelector('.card-title').textContent = title;
    el.querySelector('.card-detail').textContent = detail;
    if (!disabled) el.addEventListener('click', onPick);
    el.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      if (onDelete) onDelete();
    });
    return el;
  }

  function renderLibrary() {
    if (!libraryCell || !layout.editing) {
      closeLibrary();
      return;
    }

    const { col, row } = libraryCell;
    library.hidden = false;
    library.innerHTML = '<header><button class="back">‹ Back</button><span>Add to the deck</span></header><div class="cards"></div>';
    library.querySelector('.back').addEventListener('click', closeLibrary);
    const cards = library.querySelector('.cards');

    cards.append(card('New preset', 'save the current window layout', false, () => {
      closeLibrary();
      layoutOp({ op: 'new-preset', col, row });
    }, null));

    for (const item of layout.library) {
      for (const v of item.variants) {
        const ok = fits(v.w, v.h, col, row, null);
        cards.append(card(
          item.title,
          ok ? (item.group || v.label) : needs(v.w, v.h),
          !ok,
          () => {
            closeLibrary();
            layoutOp({ op: 'place', kind: item.kind, variant: v.variant, ref: item.ref, col, row });
          },
          item.ref ? () => layoutOp({ op: 'delete', kind: item.kind, ref: item.ref }) : null));
      }
    }
  }

  function afterRender() {
    document.body.classList.toggle('editing', layout.editing);
    drop.hidden = true;
    grid.append(drop);
    renderLibrary();
  }

  return { decorateTile, decorateEmpty, afterRender };
})();
```

- [ ] **Step 5: Style edit mode and the library**

Append to `src/Deck.Shell/ui/deck.css`:

```css
/* --- edit mode ---
   Tiles keep their normal look so you can still tell them apart; a faint wash and a × badge
   say they're movable. The shield sits over each tile's own controls, so nothing can be
   muted, started or dragged by accident while editing. */
.tile.blank { cursor: pointer; }

.tile .shield {
  display: none;
  position: absolute;
  inset: 0;
  z-index: 2;
  cursor: grab;
  border-radius: inherit;
}
body.editing .tile .shield { display: block; background: rgba(124, 196, 255, 0.05); }

.tile .remove {
  position: absolute;
  top: 6px;
  left: 6px;
  z-index: 3;
  width: 22px;
  height: 22px;
  padding: 0;
  border-radius: 50%;
  border: 1px solid var(--danger-line);
  background: var(--danger-bg);
  color: var(--danger);
  font: 700 14px/1 "Segoe UI", system-ui, sans-serif;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.tile .remove:hover { background: #4a181d; }

.tile.lifted {
  z-index: 5;
  opacity: 0.88;
  box-shadow: 0 10px 28px rgba(0, 0, 0, 0.55);
  transition: none;
}

#drop {
  pointer-events: none;
  z-index: 4;
  border-radius: 13px;
  border: 2px dashed var(--ok);
  background: rgba(63, 185, 80, 0.08);
}
#drop.bad { border-color: var(--danger); background: rgba(248, 81, 73, 0.08); }
#drop[hidden] { display: none; }

.tile.blank.add { border-color: var(--preset-line); }
.tile.blank.add .label { font-size: 26px; font-weight: 400; color: var(--preset-text); }
.tile.blank.add:hover { background: var(--preset); }

#editbar {
  display: none;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex: none;
  height: 40px;
  padding: 0 12px 10px 16px;
  color: var(--dim);
  font-size: 12px;
}
body.editing #editbar { display: flex; }

#editbar .done, #library .back {
  background: var(--preset);
  color: var(--preset-text);
  border: 1px solid var(--preset-line);
  border-radius: 8px;
  padding: 5px 18px;
  font: inherit;
  font-weight: 650;
  cursor: pointer;
}
#editbar .done:hover, #library .back:hover { background: #223041; }

/* --- library panel: covers the grid, leaves the edit bar (and Done) visible --- */
#library {
  position: absolute;
  inset: 0;
  z-index: 10;
  background: var(--bg);
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
#library[hidden] { display: none; }
#library header { display: flex; align-items: center; gap: 14px; font-size: 14px; font-weight: 650; }

#library .cards {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  scrollbar-color: var(--line) transparent;
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  grid-auto-rows: 74px;
  gap: 10px;
}
#library .card {
  background: var(--tile);
  border: 1px solid var(--line);
  border-radius: 11px;
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 4px;
  cursor: pointer;
}
#library .card:hover { background: var(--tile-hi); }
#library .card-title {
  font-size: 13px;
  font-weight: 650;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
#library .card-detail { font-size: 11px; color: var(--dim); }
#library .card.disabled { opacity: 0.4; cursor: default; }
#library .card.disabled:hover { background: var(--tile); }
```

- [ ] **Step 6: Exercise edit mode in the harness**

Reload the harness from Task 6, Step 10 (same URL, same 1077×519 size). Check each of these and take a screenshot after the ones marked 📷:

1. Click an empty cell (bottom-left). Expected: edit mode 📷 — every tile has a red × top-left, empty cells show a blue +, a bar along the bottom reads "Drag a tile to move it · …" with a **Done** button on the right, and the grid is slightly shorter to make room.
2. Click the MIC tile. Expected: `harness.log.at(-1)` is still a `layout:` message, **not** `widget:…"press"` (the shield blocks tile actions).
3. Click the + in the bottom-left cell. Expected: library panel 📷 — "‹ Back" and "Add to the deck", then cards: New preset, Pomodoro (1×1), Stopwatch (1×1), GAMING (Preset). Nothing else, because everything else is on the deck.
4. Click Pomodoro. Expected: the panel closes and the Pomodoro tile appears bottom-left, still in edit mode.
5. Click × on WEATHER, then click the + in the top-right cell. Expected: Weather shows three cards — "1×1", "1×1 compact", and "2×1 · next hours" greyed out with "needs 2 free cells here" (column 6 is the edge).
6. Click + in the top row's third cell (column 3, row 1) instead. Expected: the Weather 2×1 card is enabled (columns 3–4 are free).
7. Drag the CLOCK tile onto the CAMERA tile. Expected: a green dashed outline over CAMERA while hovering, and after release they have swapped.
8. Drag CLOCK onto the mixer. Expected: a red outline and, after release, nothing moves.
9. Drag the mixer one cell left (grab it anywhere, drop so its top-left is column 4, row 2) when that area is free, otherwise onto free space. Expected: it moves.
10. Right-click the GAMING card in the library. Expected: `harness.log.at(-1)` is `layout:{"op":"delete","kind":"preset","ref":"p2",…}`.
11. Click Done. Expected: back to normal mode 📷 — no badges or bar, empty cells dashed again.
12. `read_console_messages` with `onlyErrors: true` returns nothing.

- [ ] **Step 7: Build and test**

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q` → `Build succeeded.`
Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Passed!  - Failed: 0, Passed: 36`.

- [ ] **Step 8: Commit**

```bash
git add src/Deck.Shell
git commit -m "Add edit mode and the widget library panel" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: The new sizes — hourly and compact weather, Now Playing 2×1, Mixer 2×1

**Files:**
- Modify: `src/Deck.Shell/Weather/WeatherService.cs`
- Modify: `src/Deck.Shell/Widgets/WeatherWidget.cs` (`Push`)
- Modify: `src/Deck.Shell/Media/NowPlaying.cs`
- Modify: `src/Deck.Shell/Widgets/NowPlayingWidget.cs`
- Modify: `src/Deck.Shell/ui/widgets.js` (`ICONS`, `weather`, `nowplaying`), `src/Deck.Shell/ui/deck.css` (append)
- Test: `tests/Deck.Shell.Tests/WeatherParseTests.cs`

**Interfaces:**
- Produces:
  - `record HourlyForecast(DateTime Time, double TempC, int Code)`
  - `WeatherReading` gains a last parameter `IReadOnlyList<HourlyForecast> Hours` and a method `IReadOnlyList<HourlyForecast> Upcoming(DateTime now, int count)`
  - `static WeatherReading WeatherService.Parse(string json, DateTime fetchedAt)`
  - Weather posts gain `hourly: [{hour, icon, temp}]`
  - `NowPlaying.ArtDataUri : string?`, `NowPlaying.SkipPreviousAsync()`
  - Now Playing posts gain `art` (wide variant only, sent only when it changes); handles `prev`

- [ ] **Step 1: Write the failing weather parsing tests**

`tests/Deck.Shell.Tests/WeatherParseTests.cs`:

```csharp
using Deck.Shell.Weather;

namespace Deck.Shell.Tests;

public class WeatherParseTests
{
    private const string Sample = """
        {
          "current": { "temperature_2m": 19.4, "apparent_temperature": 17.2, "weather_code": 3 },
          "daily": { "temperature_2m_max": [20.1], "temperature_2m_min": [13.8] },
          "hourly": {
            "time": ["2026-09-23T14:00", "2026-09-23T15:00", "2026-09-23T16:00", "2026-09-23T17:00",
                     "2026-09-23T18:00", "2026-09-23T19:00", "2026-09-23T20:00", "2026-09-23T21:00"],
            "temperature_2m": [19.0, 19.5, 18.2, 17.0, null, 15.1, 14.0, 13.2],
            "weather_code": [3, 3, 2, 1, 0, 0, 0, 0]
          }
        }
        """;

    [Fact]
    public void Reads_current_conditions_and_todays_range()
    {
        var reading = WeatherService.Parse(Sample, new DateTime(2026, 9, 23, 14, 35, 0));

        Assert.Equal(19.4, reading.TempC);
        Assert.Equal(17.2, reading.FeelsC);
        Assert.Equal(20.1, reading.HighC);
        Assert.Equal(13.8, reading.LowC);
        Assert.Equal(3, reading.Code);
    }

    [Fact]
    public void Upcoming_skips_past_hours_and_gaps_and_stops_at_the_count()
    {
        var reading = WeatherService.Parse(Sample, DateTime.Now);

        var next = reading.Upcoming(new DateTime(2026, 9, 23, 14, 35, 0), 6);

        Assert.Equal(new[] { 15, 16, 17, 19, 20, 21 }, next.Select(h => h.Time.Hour));
        Assert.Equal(19.5, next[0].TempC);
    }

    [Fact]
    public void A_response_without_hourly_data_has_no_hours()
    {
        const string json = """
            {
              "current": { "temperature_2m": 1, "apparent_temperature": 1, "weather_code": 0 },
              "daily": { "temperature_2m_max": [2], "temperature_2m_min": [0] }
            }
            """;

        Assert.Empty(WeatherService.Parse(json, DateTime.Now).Hours);
    }
}
```

- [ ] **Step 2: Run them to confirm they fail**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS — `WeatherService` has no `Parse`, `WeatherReading` has no `Hours`.

- [ ] **Step 3: Add the hourly forecast to the weather service**

In `src/Deck.Shell/Weather/WeatherService.cs`:

Add `using System.Globalization;` at the top.

Replace the `WeatherReading` record with:

```csharp
internal sealed record HourlyForecast(DateTime Time, double TempC, int Code);

internal sealed record WeatherReading(
    double TempC,
    double FeelsC,
    double HighC,
    double LowC,
    int Code,
    DateTime FetchedAt,
    IReadOnlyList<HourlyForecast> Hours)
{
    /// <summary>
    /// The next few hours after <paramref name="now"/>. Worked out at display time rather than
    /// fetch time, so the list moves on between the 15-minute fetches.
    /// </summary>
    public IReadOnlyList<HourlyForecast> Upcoming(DateTime now, int count) =>
        Hours.Where(h => h.Time > now).Take(count).ToList();
}
```

In the `Url` field, replace the last two lines:

```csharp
        "&daily=temperature_2m_max,temperature_2m_min" +
        "&timezone=Europe%2FIstanbul&forecast_days=1";
```

with (two days, so late in the evening there are still six hours ahead):

```csharp
        "&daily=temperature_2m_max,temperature_2m_min" +
        "&hourly=temperature_2m,weather_code" +
        "&timezone=Europe%2FIstanbul&forecast_days=2";
```

In `RefreshAsync`, replace everything from `using var document = JsonDocument.Parse(json);` down to and including `Error = null;` with:

```csharp
            Latest = Parse(json, DateTime.Now);
            Error = null;
```

Add this method after `RefreshAsync`:

```csharp
    /// <summary>
    /// Reads an Open-Meteo response. Separate from the fetch so it can be tested without the
    /// network. Hours with a missing value are skipped rather than shown as zero.
    /// </summary>
    public static WeatherReading Parse(string json, DateTime fetchedAt)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var current = root.GetProperty("current");
        var daily = root.GetProperty("daily");

        var hours = new List<HourlyForecast>();
        if (root.TryGetProperty("hourly", out var hourly))
        {
            var times = hourly.GetProperty("time");
            var temps = hourly.GetProperty("temperature_2m");
            var codes = hourly.GetProperty("weather_code");
            int count = Math.Min(times.GetArrayLength(), Math.Min(temps.GetArrayLength(), codes.GetArrayLength()));

            for (int i = 0; i < count; i++)
            {
                if (temps[i].ValueKind != JsonValueKind.Number || codes[i].ValueKind != JsonValueKind.Number) continue;
                if (!DateTime.TryParse(times[i].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) continue;

                hours.Add(new HourlyForecast(time, temps[i].GetDouble(), codes[i].GetInt32()));
            }
        }

        return new WeatherReading(
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("apparent_temperature").GetDouble(),
            daily.GetProperty("temperature_2m_max")[0].GetDouble(),
            daily.GetProperty("temperature_2m_min")[0].GetDouble(),
            current.GetProperty("weather_code").GetInt32(),
            fetchedAt,
            hours);
    }
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: `Passed!  - Failed: 0, Passed: 39`.

- [ ] **Step 5: Send the hours from the weather widget**

In `src/Deck.Shell/Widgets/WeatherWidget.cs`, add `using System.Globalization;` and, in `Push`, add after the `error = _weather.Error` line (put a comma after `error = _weather.Error`):

```csharp
            hourly = (reading?.Upcoming(DateTime.Now, 6) ?? []).Select(h => new
            {
                hour = h.Time.ToString("HH", CultureInfo.InvariantCulture),
                icon = WeatherService.Describe(h.Code).Icon,
                temp = $"{Math.Round(h.TempC)}°"
            }).ToArray()
```

- [ ] **Step 6: Add album art and previous-track to NowPlaying**

In `src/Deck.Shell/Media/NowPlaying.cs`:

Add `using Windows.Storage.Streams;` at the top.

Add after the `HasSession` property:

```csharp
    /// <summary>Album art as a data: URI, or null when the source doesn't publish any.</summary>
    public string? ArtDataUri { get; private set; }

    private string _artTrack = "";
```

In `RefreshAsync`, after `HasSession = true;`, add:

```csharp
            await RefreshArtAsync(properties.Thumbnail);
```

Add after `SkipNextAsync`:

```csharp
    public async Task SkipPreviousAsync()
    {
        try
        {
            var session = _manager?.GetCurrentSession();
            if (session is not null) await session.TrySkipPreviousAsync();
        }
        catch { }
    }

    /// <summary>
    /// Read once per track rather than every poll, since the image is tens of kilobytes. A track
    /// with no art yet is retried: players often publish the title a moment before the art.
    /// </summary>
    private async Task RefreshArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        string track = $"{App}|{Title}|{Artist}";
        if (track == _artTrack) return;

        ArtDataUri = await ReadArtAsync(thumbnail);
        if (ArtDataUri is not null) _artTrack = track;
    }

    private static async Task<string?> ReadArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;

        try
        {
            using var stream = await thumbnail.OpenReadAsync();

            // Anything this large isn't cover art worth pushing through the page every track.
            if (stream.Size == 0 || stream.Size > 2_000_000) return null;

            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);

            string type = string.IsNullOrEmpty(stream.ContentType) ? "image/png" : stream.ContentType;
            return $"data:{type};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }
```

In `Clear()`, add:

```csharp
        ArtDataUri = null;
        _artTrack = "";
```

- [ ] **Step 7: Send art and handle previous in the widget**

Replace the whole of `src/Deck.Shell/Widgets/NowPlayingWidget.cs` with:

```csharp
using Deck.Shell.Media;

namespace Deck.Shell.Widgets;

internal sealed class NowPlayingWidget(WidgetContext context) : WidgetBase(context, "nowplaying")
{
    /// <summary>The art the page already has. Art only travels when it changes — it's the one heavy field.</summary>
    private string? _artSent;

    private NowPlaying Media => Context.Media.NowPlaying;

    /// <summary>Only the 2×1 tile shows art; the 1×1 never needs it sent.</summary>
    private bool ShowsArt => Variant == "wide";

    public override void Start()
    {
        Context.Media.Refreshed += OnRefreshed;
        Context.Media.Acquire();
    }

    public override void Stop()
    {
        Context.Media.Refreshed -= OnRefreshed;
        Context.Media.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                _ = Media.TogglePlayPauseAsync();
                return true;

            case "next":
                _ = Media.SkipNextAsync();
                return true;

            case "prev":
                _ = Media.SkipPreviousAsync();
                return true;

            default:
                return false;
        }
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "nowplaying") return false;

        _ = Media.TogglePlayPauseAsync();
        return true;
    }

    public override void Push() => Send(includeArt: ShowsArt);

    private void OnRefreshed() => Send(includeArt: ShowsArt && Media.ArtDataUri != _artSent);

    private void Send(bool includeArt)
    {
        var data = new Dictionary<string, object?>
        {
            ["hasSession"] = Media.HasSession,
            ["playing"] = Media.IsPlaying,
            ["title"] = Media.Title,
            ["artist"] = Media.Artist,
            ["app"] = Media.App
        };

        if (includeArt)
        {
            data["art"] = Media.ArtDataUri;
            _artSent = Media.ArtDataUri;
        }

        Post(data);
    }
}
```

- [ ] **Step 8: Build and test**

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q` → `Build succeeded.`
Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Passed!  - Failed: 0, Passed: 39`.

- [ ] **Step 9: Render the new sizes in the page**

In `src/Deck.Shell/ui/widgets.js`, add these entries to the `ICONS` object (after `cameraOff`, with a comma after its closing backtick):

```js
  play: `<svg class="i-play" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" fill="currentColor" /></svg>`,
  pause: `<svg class="i-pause" viewBox="0 0 24 24"><path d="M7 5h4v14H7zM13 5h4v14h-4z" fill="currentColor" /></svg>`,
  prev: `<svg viewBox="0 0 24 24"><path d="M6 5h2v14H6zM20 5v14L9 12z" fill="currentColor" /></svg>`,
  next: `<svg viewBox="0 0 24 24"><path d="M16 5h2v14h-2zM4 5v14l11-7z" fill="currentColor" /></svg>`
```

Add this function after `renderMixer`:

```js
function renderHours(host, hours) {
  host.innerHTML = '';
  if (!hours.length) {
    host.innerHTML = '<div class="wx-none">no forecast</div>';
    return;
  }
  for (const h of hours) {
    const cell = document.createElement('div');
    cell.className = 'wx-hour';
    cell.innerHTML = '<span class="wx-h"></span><span class="wx-hi"></span><span class="wx-ht"></span>';
    cell.children[0].textContent = h.hour;
    cell.children[1].textContent = h.icon;
    cell.children[2].textContent = h.temp;
    host.append(cell);
  }
}

const WEATHER_STANDARD = `
  <div class="label">ANKARA</div>
  <div class="wx-now"><span class="wx-icon">–</span><span class="wx-temp">–</span></div>
  <div class="sub wx-label">loading…</div>
  <div class="wx-stats">
    <div><span class="wx-k">FEELS</span><span class="wx-v wx-feels">–</span></div>
    <div><span class="wx-k">HIGH</span><span class="wx-v wx-high">–</span></div>
    <div><span class="wx-k">LOW</span><span class="wx-v wx-low">–</span></div>
  </div>`;

const WEATHER_COMPACT = `
  <div class="label">ANKARA</div>
  <div class="wx-now"><span class="wx-icon">–</span><span class="wx-temp">–</span></div>
  <div class="sub wx-label">loading…</div>`;

const NP_TEXT = `
  <div class="np-title">nothing</div>
  <div class="sub np-artist"></div>
  <div class="device np-app"></div>`;
```

Replace the whole `weather: { … },` entry in `Widgets` with:

```js
  weather: {
    template: (variant) =>
      variant === 'compact' ? WEATHER_COMPACT
      : variant === 'hourly' ? `<div class="wx-main">${WEATHER_STANDARD}</div><div class="wx-hours"></div>`
      : WEATHER_STANDARD,
    update(tile, d, variant) {
      tile.classList.toggle('stale', d.stale);
      setText(tile, '.wx-icon', d.icon);
      setText(tile, '.wx-temp', d.temp);
      setText(tile, '.wx-label', d.error || d.label);
      if (variant === 'compact') return;
      setText(tile, '.wx-feels', d.available ? d.feels : '–');
      setText(tile, '.wx-high', d.available ? d.high : '–');
      setText(tile, '.wx-low', d.available ? d.low : '–');
      if (variant === 'hourly') renderHours(q(tile, '.wx-hours'), d.hourly || []);
    }
  },
```

Replace the whole `nowplaying: { … },` entry with:

```js
  nowplaying: {
    click: (variant) => (variant === 'wide' ? null : 'press'),
    context: (variant) => (variant === 'wide' ? null : 'next'),
    template: (variant) => variant === 'wide'
      ? `<div class="np-art">${ICONS.record}<img alt="" hidden></div>
         <div class="np-body">
           ${NP_TEXT}
           <div class="np-controls">
             <button class="np-btn" data-msg="prev" title="Previous">${ICONS.prev}</button>
             <button class="np-btn np-play" data-msg="press" title="Play / pause">${ICONS.play}${ICONS.pause}</button>
             <button class="np-btn" data-msg="next" title="Next">${ICONS.next}</button>
           </div>
         </div>`
      : `${ICONS.record}${NP_TEXT}`,
    bind(tile, send, variant) {
      if (variant !== 'wide') return;
      for (const button of tile.querySelectorAll('.np-btn')) {
        button.addEventListener('click', (e) => {
          e.stopPropagation();
          send(button.dataset.msg);
        });
      }
    },
    update(tile, d, variant) {
      tile.classList.toggle('playing', d.playing);
      tile.classList.toggle('idle', !d.hasSession);
      setText(tile, '.np-title', d.hasSession ? (d.title || 'untitled') : 'nothing playing');
      setText(tile, '.np-artist', !d.hasSession ? '' : d.playing ? (d.artist || 'playing') : 'paused');
      setText(tile, '.np-app', d.app);
      if (variant === 'wide' && 'art' in d) {
        const img = q(tile, '.np-art img');
        img.hidden = !d.art;
        if (d.art) img.src = d.art;
        else img.removeAttribute('src');
        q(tile, '.np-art').classList.toggle('has-art', !!d.art);
      }
    }
  },
```

- [ ] **Step 10: Style the new sizes**

Append to `src/Deck.Shell/ui/deck.css`:

```css
/* --- weather 1x1 compact: readable from across the desk --- */
.w-weather.v-compact { gap: 8px; }
.w-weather.v-compact .wx-icon { font-size: 36px; }
.w-weather.v-compact .wx-temp { font-size: 44px; }

/* --- weather 2x1: today on the left, the next six hours on the right --- */
.tile.w-weather.v-hourly { flex-direction: row; align-items: stretch; gap: 14px; }
.w-weather.v-hourly .wx-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 7px;
}
.w-weather.v-hourly .wx-hours {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  grid-template-rows: repeat(2, 1fr);
  gap: 6px;
  padding-left: 14px;
  border-left: 1px solid var(--line);
}
.wx-hour { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 3px; }
.wx-h { font-size: 10px; font-weight: 600; color: var(--dim); font-variant-numeric: tabular-nums; }
.wx-hi { font-family: "Segoe UI Emoji", "Apple Color Emoji", system-ui; font-size: 18px; line-height: 1; }
.wx-ht { font-size: 13px; font-weight: 650; font-variant-numeric: tabular-nums; }
.wx-none { grid-column: 1 / -1; grid-row: 1 / -1; align-self: center; text-align: center; font-size: 12px; color: var(--dim); }

/* --- now playing 2x1: art, text and real buttons; the tile itself isn't a button --- */
.tile.w-nowplaying.v-wide { flex-direction: row; justify-content: flex-start; gap: 14px; padding: 12px 14px; text-align: left; }
.w-nowplaying.v-wide .np-art {
  flex: none;
  height: 100%;
  max-height: 132px;
  aspect-ratio: 1;
  border-radius: 10px;
  background: #0e1116;
  border: 1px solid var(--line);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}
.w-nowplaying.v-wide .np-art img { width: 100%; height: 100%; object-fit: cover; }
.w-nowplaying.v-wide .np-art .np-record { width: 56%; height: 56%; }
.w-nowplaying.v-wide .np-art.has-art .np-record { display: none; }
.w-nowplaying.v-wide .np-body { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 4px; }
.w-nowplaying.v-wide .np-title { font-size: 14px; }
.w-nowplaying.v-wide .sub { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

.np-controls { display: flex; gap: 8px; margin-top: 8px; }
.np-btn {
  width: 38px;
  height: 30px;
  padding: 0;
  border-radius: 8px;
  border: 1px solid var(--line);
  background: #0e1116;
  color: var(--text);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  font: inherit;
}
.np-btn:hover { background: var(--tile-hi); }
.np-btn:active { transform: scale(0.95); }
.tile .np-btn svg { width: 16px; height: 16px; }
.w-nowplaying .i-pause { display: none; }
.w-nowplaying.playing .i-pause { display: block; }
.w-nowplaying.playing .i-play { display: none; }
.w-nowplaying.playing .np-play { color: var(--ok); border-color: var(--ok-line); }

/* --- mixer 2x1: three rows in one row's height --- */
.w-mixer.v-short .mx-barwrap { padding: 5px 0; }
```

- [ ] **Step 11: Check the new sizes in the harness**

Reload the harness (1077×519). Using edit mode:
1. Remove WEATHER, then add "Weather · 2×1 · next hours" at column 3, row 1 (the empty top-row cells). 📷 Expected: today's weather on the left; on the right a 3×2 grid of hours 15–20, each with its icon and temperature.
2. Remove it, then add "Weather · 1×1 compact" at the top right. 📷 Expected: a big cloud and "19°", "Overcast" below, and no FEELS/HIGH/LOW row.
3. Remove NOW PLAYING, then add "Now Playing · 2×1 · art & controls" in the bottom-left two cells. 📷 Expected: a blue square (the harness's sample art) on the left; "Teardrop", "Massive Attack", "Spotify"; and three buttons, with the middle one green and showing a pause icon. After Done, click the ⏭ button → `harness.log.at(-1)` is `widget:{"kind":"nowplaying","ref":null,"msg":"next"}`. Click the tile's text area → no new `widget:` message.
4. Remove MIXER, then add "Mixer · 2×1 · 3 apps" at column 5, row 2. 📷 Expected: a half-height mixer. (The harness still sends six rows; the real host sends three. Check that the rows fit, or at least clip cleanly, and don't overflow the tile's rounded border.)
5. `read_console_messages` with `onlyErrors: true` returns nothing.

- [ ] **Step 12: Commit**

```bash
git add src/Deck.Shell tests/Deck.Shell.Tests/WeatherParseTests.cs
git commit -m "Add hourly and compact weather, wide now-playing and short mixer sizes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Shortcuts window labels, README, install and verify on the real deck

**Files:**
- Modify: `src/Deck.Shell/HotkeyWindow.xaml.cs` (constructor, `Actions()`)
- Modify: `src/Deck.Shell/MainWindow.xaml.cs` (`OpenHotkeys`, new `IsOnDeck`)
- Modify: `README.md`

**Interfaces:**
- Consumes: `WidgetCatalog.OwnerOf`, `DeckLayout.IsPlaced`.
- Produces: `HotkeyWindow(DeckConfig config, Func<string, bool> isOnDeck)`.

- [ ] **Step 1: Mark hotkeys whose widget is in the library**

In `src/Deck.Shell/HotkeyWindow.xaml.cs`:

Add a field after `_conflicts`:

```csharp
    private readonly Func<string, bool> _isOnDeck;
```

Replace the constructor with:

```csharp
    /// <param name="isOnDeck">
    /// Whether an action's widget is on the deck. Bindings for widgets in the library are kept
    /// but marked, since pressing them does nothing until the widget is back.
    /// </param>
    internal HotkeyWindow(DeckConfig config, Func<string, bool> isOnDeck)
    {
        InitializeComponent();
        _config = config;
        _isOnDeck = isOnDeck;
        Loaded += OnLoaded;
    }
```

Rename the existing `Actions()` method to `AllActions()` (body unchanged), and add above it:

```csharp
    /// <summary>Every action the deck can expose to a shortcut, marked when its widget is off the deck.</summary>
    private IEnumerable<(string Action, string Label)> Actions() =>
        AllActions().Select(a => (a.Action, _isOnDeck(a.Action) ? a.Label : a.Label + "  (not on deck)"));
```

In `src/Deck.Shell/MainWindow.xaml.cs`, in `OpenHotkeys`, replace `_hotkeyWindow = new HotkeyWindow(_config);` with `_hotkeyWindow = new HotkeyWindow(_config, IsOnDeck);`, and add this method after `RunAction`:

```csharp
    /// <summary>Whether a hotkey's widget is on the deck; hotkeys for widgets in the library do nothing.</summary>
    private bool IsOnDeck(string action) =>
        WidgetCatalog.OwnerOf(action) is { } owner && new DeckLayout(_config.Layout).IsPlaced(owner.Kind, owner.Ref);
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q` → `Build succeeded.`
Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Passed!  - Failed: 0, Passed: 39`.

- [ ] **Step 3: Update the README**

In `README.md`:

Replace the line `- **Tray icon** → Microphones…, Shortcuts…, Start with Windows, Exit` with:

```markdown
- **Tray icon** → Edit layout, Microphones…, Shortcuts…, Start with Windows, Exit
```

Replace the whole `## The tiles` section (heading, table, and the paragraph after it) with:

```markdown
## The tiles

| Tile | Click | Right-click |
|---|---|---|
| Preset (e.g. WORK) | restore that window layout | delete it |
| Shortcut | open it | delete it |
| CLAUDE | jump to a session that wants you | mute/unmute its notifications |
| WEATHER | — | — |
| NOW PLAYING | play/pause | next track |
| NOW PLAYING 2×1 | its buttons: previous, play/pause, next | — |
| MIC | mute/unmute your microphone | — |
| CAMERA | — | — |
| NOISE | arm/disarm the alert | recalibrate the limit |
| POMODORO | start/stop a block | reset the counter |
| STOPWATCH | start/stop | reset |
| MIXER row | mute that app (icon or name) | forget the app |
| MIXER bar | drag to set volume | — |
| MIXER tile | — | open the full mixer |
| Empty cell | edit the layout | — |

Any tile action can also be bound to a global keyboard shortcut — tray → **Shortcuts…**. The
screen is for state; the keyboard is for speed.

## Editing the layout

Click an empty cell, or tray → **Edit layout**. In edit mode:

- **×** sends a tile to the library. Nothing is deleted, and its settings are kept.
- **Drag** a tile to move it. Drop it on a tile of the same size and the two swap.
- **+** on an empty cell opens the library: every widget not on the deck, in each size it comes
  in (Weather 1×1, compact or 2×1 with the next hours; Now Playing 1×1 or 2×1 with art and
  buttons; Mixer 2×2 or 2×1), your presets and shortcuts not on the deck, and **New preset**.
  A size that doesn't fit at that cell is greyed out.
- **Done**, bottom right, leaves edit mode.

A widget in the library is fully off: no polling, no listening, no notifications, and its
keyboard shortcut does nothing (the Shortcuts window marks it "not on deck"). Removing the mic
tile leaves the microphone as it was.
```

In the `## Rebuilding` section, directly after the publish command block (the one ending `-o "$env:LOCALAPPDATA\Deck\app"`) and before the "Stop the running deck first" paragraph, add:

````markdown
Tests (the layout rules, the migration from older configs, the widget host):

```bash
dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj
```

To work on the page without replacing the running deck, open `src/Deck.Shell/ui/dev/harness.html`
in a browser. It fakes the host with sample data.
````

- [ ] **Step 4: Commit**

```bash
git add src/Deck.Shell README.md
git commit -m "Mark off-deck shortcuts and document the widget library" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Back up the live config and find the install**

```powershell
$exe = (Get-Process Deck -ErrorAction Stop).Path
$installDir = Split-Path $exe
$backup = "C:\Users\justb\AppData\Local\Temp\claude\C--Users-justb-Workspace-Personal-deck\48f6fe54-d2f1-42ec-b0fe-efda5f074abf\scratchpad\config.backup.json"
Copy-Item "$env:APPDATA\Deck\config.json" $backup
$installDir
```

Keep `$installDir`. This is the folder the running deck was actually started from, which may be under a virtualised `Packages\…\LocalCache` path, so it's the one to publish over. If `config.json` isn't at `$env:APPDATA\Deck\`, look for `Deck\config.json` under `$env:LOCALAPPDATA\Packages\*\LocalCache\Roaming\` and back that one up instead.

- [ ] **Step 6: Stop the running deck gracefully**

Run: `taskkill /IM Deck.exe` (no `/F`). Then poll `Get-Process Deck -ErrorAction SilentlyContinue` for up to 10 seconds.
Expected: the process exits and the reserved strip at the bottom of the monitor is released.
If it doesn't exit, **don't force it**. Ask the user: "Please right-click the Deck tray icon → Exit deck, then tell me." Wait for them.

- [ ] **Step 7: Publish over the install**

```powershell
dotnet publish src/Deck.Shell/Deck.Shell.csproj -c Release -r win-x64 --self-contained true -o $installDir -nologo
```

Expected: ends with `Deck -> <installDir>\`. Check `Test-Path "$installDir\ui\edit.js"` is `True` and `Test-Path "$installDir\ui\dev"` is `False`.

- [ ] **Step 8: Have the user start the deck**

Don't start it from this shell: a process launched from here can inherit this app's file-system virtualisation and read or write a different `config.json`. Ask the user: "Please start Deck from the Start menu (type Deck)." Wait for their confirmation.

- [ ] **Step 9: Verify on the real deck**

Take a screenshot of the deck's monitor (PowerShell, `System.Windows.Forms.Screen` + `Graphics.CopyFromScreen` into a PNG in the scratchpad) and view it with Read. Expected: the same deck as before, with the bottom-left two cells empty and dashed, and live data in every tile (weather, mixer rows, clock times, mic/camera state).

Read `$env:APPDATA\Deck\config.json` (or the location found in Step 5). Expected: `"LayoutInitialised": true`; a `"Layout"` array of 9 or more placements with no `pomodoro`/`stopwatch`; every preset has an `"Id"`; any preset hotkey reads `preset:<32-hex-id>`.

Then ask the user to try, and report back on:
1. Click an empty cell → edit mode appears, and the app they were typing in **keeps keyboard focus**.
2. Click + → the library shows Pomodoro, Stopwatch and New preset.
3. Add Pomodoro, drag it somewhere, remove it again, click Done.
4. Mute via their mic hotkey still works.

If anything is wrong, stop the deck first, then restore with `Copy-Item $backup <the config path from Step 5>` and debug with superpowers:systematic-debugging.

- [ ] **Step 10: Push**

```bash
git push
```
