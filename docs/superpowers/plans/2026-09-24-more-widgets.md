# More Widgets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add six widgets to the deck's library — Dice, Countdown (many), Network, Display (brightness + warm tint), Agenda and Month — plus the shared iCal calendar connection.

**Architecture:** Each widget follows the existing pattern: a catalogue entry (`Layout/WidgetCatalog.cs`), an `IWidget` class under `Widgets/` started and stopped by `WidgetHost`, pure logic in its own tested file, and a page renderer. New renderers live in their own files under `ui/widgets/<kind>.js` and `.css` (registering on the shared `Widgets` object), instead of growing `widgets.js`. Countdowns are per-item tiles like presets. Agenda and Month share a reference-counted `CalendarService`.

**Tech Stack:** .NET 10 WPF (`net10.0-windows10.0.19041.0`, x64), WebView2, xUnit 2, **Ical.Net 5.2.3** (new), Win32 dxva2 (DDC/CI brightness) and gdi32 (gamma ramps) via P/Invoke, plain HTML/CSS/JS.

**Spec:** `docs/superpowers/specs/2026-09-24-more-widgets-design.md`

## Global Constraints

- **The deck never takes keyboard focus.** Anything needing a keyboard opens its own ordinary window (like `CaptureWindow`). Only the countdown and calendar windows are new.
- **No new comments in `.js` files, and none in the inline `<script>` of new `.html` pages.** This is an organisation rule. C# and CSS comments follow the existing style: explanatory `///` summaries on non-obvious *why*.
- Grid is 6×3; new kinds, exactly: `dice` (`standard` 1×1), `agenda` (`standard` 1×1, `wide` 2×1), `month` (`standard` 2×2), `network` (`standard` 1×1), `display` (`standard` 1×1), and `countdown` (per item, `standard` 1×1).
- New page renderers go in `src/Deck.Shell/ui/widgets/<kind>.js` + `<kind>.css`. Each is loaded from `deck.html` **and** `ui/dev/harness.html`.
- iCal links are secrets. They're stored in `config.json` and fetched from their own server only. They are never logged and never shown on the deck. The Calendar window's status list shows them masked.
- The network widget pings `1.1.1.1` only. It never runs a speed test.
- Warm tint ramp: red ×1.0, green ×0.85, blue ×0.65. `GammaTint.Reset()` only undoes a tint the deck applied.
- Build: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q` must give 0 errors and 0 warnings. Tests: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` must pass with 0 failed.
- **Do not run, stop or publish the Deck app.** Check the page through the harness: start the `deck-ui` configuration in `.claude/launch.json` with `mcp__Claude_Browser__preview_start`, then open `http://localhost:5178/dev/harness.html` at 1077×519. The file:// URL does not run scripts in the browser pane.
- Commit with exactly `git commit -m "<subject>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`. Use that trailer verbatim.

## File Structure

```
src/Deck.Shell/
  Layout/WidgetCatalog.cs          + dice, agenda, month, network, display, countdown
  Layout/DeckLayout.cs             Validate(itemExists) generalised
  Layout/LayoutMigration.cs        uses DeckConfig.HasItem
  Config/DeckConfig.cs             + Countdowns, CalendarLinks, DiceMode, DisplayTint, HasItem
  Countdowns/Countdown.cs          the saved item
  Countdowns/CountdownText.cs      what the tile says (pure)
  Countdowns/CountdownInput.cs     form → Countdown (pure)
  Network/NetworkHealth.cs         state + rate text (pure)
  Network/NetworkCounters.cs       adapter byte counters
  Interop/DisplayNative.cs         dxva2 + gdi32 P/Invoke
  Display/MonitorBrightness.cs     DDC/CI read/set
  Display/BrightnessShift.cs       "shift together" maths (pure)
  Display/GammaTint.cs             warm ramp apply/reset
  Calendars/CalendarEntry.cs       one local-time occurrence
  Calendars/MeetingLinks.cs        join-link detection (pure)
  Calendars/CalendarParser.cs      Ical.Net → entries
  Calendars/CalendarService.cs     shared fetch/refresh
  Calendars/AgendaText.cs          agenda "when"/state (pure)
  Calendars/MonthGrid.cs           42-day grid (pure)
  Widgets/{Dice,Countdown,Network,Display,Agenda,Month}Widget.cs
  Widgets/WidgetContext.cs         + Calendar
  Widgets/WidgetFactory.cs         + new kinds
  CountdownWindow.xaml(.cs), ui/countdown.html
  CalendarWindow.xaml(.cs), ui/calendar.html
  MainWindow.xaml.cs               library items, layout ops, tray Calendar…, service lifetime
  App.xaml.cs                      reset tint on any exit
  ui/deck.html, ui/deck.js, ui/edit.js, ui/dev/harness.html, ui/dev/harness.js
  ui/widgets/{countdown,dice,network,display,agenda,month}.{js,css}
tests/Deck.Shell.Tests/
  CountdownTextTests.cs, CountdownInputTests.cs, DiceTests.cs, NetworkHealthTests.cs,
  BrightnessShiftTests.cs, GammaTintTests.cs, MeetingLinksTests.cs, CalendarParserTests.cs,
  AgendaTextTests.cs, MonthGridTests.cs (+ edits to DeckLayoutTests.cs, LayoutMigrationTests.cs)
```

---

### Task 1: Catalogue, config and generic per-item validation

**Files:**
- Modify: `src/Deck.Shell/Layout/WidgetCatalog.cs`, `src/Deck.Shell/Layout/DeckLayout.cs`, `src/Deck.Shell/Layout/LayoutMigration.cs`, `src/Deck.Shell/Config/DeckConfig.cs`
- Create: `src/Deck.Shell/Countdowns/Countdown.cs`
- Modify: `src/Deck.Shell/ui/dev/harness.js`
- Test: `tests/Deck.Shell.Tests/DeckLayoutTests.cs`, `tests/Deck.Shell.Tests/LayoutMigrationTests.cs`

**Interfaces:**
- Produces:
  - `Countdown { string Id; string Label = "COUNTDOWN"; DateTime Target; bool HasTime }` in namespace `Deck.Shell.Countdowns`
  - `DeckConfig.Countdowns : List<Countdown>`, `CalendarLinks : List<string>`, `DiceMode : string = "d6"`, `DisplayTint : bool`, `bool HasItem(string kind, string id)`
  - `DeckLayout.Validate(Func<string, string, bool> itemExists) : int`, which replaces the two-set overload
  - Harness `CATALOG` / `ITEMS` tables, used by later tasks' harness edits

- [ ] **Step 1: Write the failing tests**

In `tests/Deck.Shell.Tests/DeckLayoutTests.cs`, replace this line in `Validate_drops_what_the_deck_cannot_honour`:

```csharp
        int dropped = layout.Validate(new HashSet<string> { "kept" }, new HashSet<string>());
```

with:

```csharp
        int dropped = layout.Validate((kind, id) => kind == "preset" && id == "kept");
```

and add these tests to the class:

```csharp
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
```

In `tests/Deck.Shell.Tests/LayoutMigrationTests.cs`, add `using Deck.Shell.Countdowns;` and this test:

```csharp
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
```

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS. There's no `Validate(Func<…>)` overload, no `Deck.Shell.Countdowns`, and no `DeckConfig.Countdowns` / `HasItem`.

- [ ] **Step 3: Add the countdown model**

`src/Deck.Shell/Countdowns/Countdown.cs`:

```csharp
namespace Deck.Shell.Countdowns;

/// <summary>A date the user is counting down to. One tile each, like presets.</summary>
internal sealed class Countdown
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Label { get; set; } = "COUNTDOWN";

    /// <summary>Local wall-clock time. Midnight of the day when <see cref="HasTime"/> is false.</summary>
    public DateTime Target { get; set; }

    /// <summary>Whether a time of day was given. Date-only countdowns count in whole days.</summary>
    public bool HasTime { get; set; }
}
```

- [ ] **Step 4: Extend the config**

In `src/Deck.Shell/Config/DeckConfig.cs`, add after the `LayoutInitialised` property:

```csharp
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
```

- [ ] **Step 5: Generalise validation**

In `src/Deck.Shell/Layout/DeckLayout.cs`, replace the whole `Validate` method, including its `///` summary, with:

```csharp
    /// <summary>
    /// Drops every placement the deck can't honour: an unknown kind or size, a second copy of a
    /// built-in, a per-item tile whose item no longer exists, anything off the grid or
    /// overlapping an earlier tile. Dropped widgets simply end up in the library, so a damaged
    /// config never stops the deck starting. Returns how many were dropped.
    /// </summary>
    /// <param name="itemExists">Asked once per per-item tile, with its kind and reference.</param>
    public int Validate(Func<string, string, bool> itemExists)
    {
        var kept = new DeckLayout();

        foreach (var p in _placements)
        {
            bool referenceExists = WidgetCatalog.Find(p.Kind) is not { PerItem: true }
                || (p.Ref is not null && itemExists(p.Kind, p.Ref));

            if (referenceExists) kept.Place(p.Kind, p.Variant, p.Ref, p.Col, p.Row);
        }

        int dropped = _placements.Count - kept._placements.Count;
        _placements.Clear();
        _placements.AddRange(kept._placements);
        return dropped;
    }
```

In `src/Deck.Shell/Layout/LayoutMigration.cs`, replace:

```csharp
        int dropped = layout.Validate(
            config.Presets.Select(p => p.Id).ToHashSet(StringComparer.Ordinal),
            config.Shortcuts.Select(s => s.Id).ToHashSet(StringComparer.Ordinal));
```

with:

```csharp
        int dropped = layout.Validate(config.HasItem);
```

- [ ] **Step 6: Add the new kinds to the catalogue**

In `src/Deck.Shell/Layout/WidgetCatalog.cs`, in `Kinds`, insert after the `new("stopwatch", …)` line:

```csharp
        new("dice", "Dice", false, [Cell()]),
        new("agenda", "Agenda", false,
        [
            new(Standard, "1×1 · next event", 1, 1),
            new("wide", "2×1 · next 3", 2, 1)
        ]),
        new("month", "Month", false, [new(Standard, "2×2", 2, 2)]),
        new("network", "Network", false, [Cell()]),
        new("display", "Display", false, [Cell()]),
```

and change the last entry `new("shortcut", "Shortcut", true, [Cell()])` to:

```csharp
        new("shortcut", "Shortcut", true, [Cell()]),
        new("countdown", "Countdown", true, [Cell()])
```

- [ ] **Step 7: Run the tests to confirm they pass**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: `Passed!` with `Failed: 0`, and the three new tests among them.

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 8: Give the harness a catalogue it can grow**

In `src/Deck.Shell/ui/dev/harness.js`, replace everything from the line `const SIZES = {` down to and including the closing `};` of the `const LABELS = {` object with (no comments; JS rule):

```js
  const CATALOG = {
    claude: ['Claude', { standard: [1, 1, '1×1'] }],
    weather: ['Weather', { standard: [1, 1, '1×1'], compact: [1, 1, '1×1 compact'], hourly: [2, 1, '2×1 · next hours'] }],
    nowplaying: ['Now Playing', { standard: [1, 1, '1×1'], wide: [2, 1, '2×1 · art & controls'] }],
    system: ['System', { standard: [1, 1, '1×1'] }],
    noise: ['Noise', { standard: [1, 1, '1×1'] }],
    mic: ['Mic', { standard: [1, 1, '1×1'] }],
    camera: ['Camera', { standard: [1, 1, '1×1'] }],
    clock: ['World Clock', { standard: [1, 1, '1×1'] }],
    mixer: ['Mixer', { tall: [2, 2, '2×2 · 6 apps'], short: [2, 1, '2×1 · 3 apps'] }],
    pomodoro: ['Pomodoro', { standard: [1, 1, '1×1'] }],
    stopwatch: ['Stopwatch', { standard: [1, 1, '1×1'] }],
    dice: ['Dice', { standard: [1, 1, '1×1'] }],
    agenda: ['Agenda', { standard: [1, 1, '1×1 · next event'], wide: [2, 1, '2×1 · next 3'] }],
    month: ['Month', { standard: [2, 2, '2×2'] }],
    network: ['Network', { standard: [1, 1, '1×1'] }],
    display: ['Display', { standard: [1, 1, '1×1'] }]
  };
  const ITEMS = { preset: 'Preset', shortcut: 'Shortcut', countdown: 'Countdown' };
  const SIZES = {};
  for (const [kind, [, variants]] of Object.entries(CATALOG)) {
    SIZES[kind] = {};
    for (const [variant, [w, h]] of Object.entries(variants)) SIZES[kind][variant] = [w, h];
  }
  for (const kind of Object.keys(ITEMS)) SIZES[kind] = { standard: [1, 1] };
```

Then in `sendLayout`, replace:

```js
    const builtIns = Object.keys(TITLES).filter((k) => !placed(k, null)).map((k) => ({
      kind: k, ref: null, title: TITLES[k], group: null,
      variants: Object.entries(SIZES[k]).map(([variant, [w, h]]) => ({ variant, label: LABELS[variant], w, h }))
    }));
```

with:

```js
    const builtIns = Object.entries(CATALOG).filter(([k]) => !placed(k, null)).map(([k, [title, variants]]) => ({
      kind: k, ref: null, title, group: null,
      variants: Object.entries(variants).map(([variant, [w, h, label]]) => ({ variant, label, w, h }))
    }));
```

Check: start `deck-ui`, open the harness, click an empty cell, then click a `+`. The library lists Pomodoro, Stopwatch, **Dice, Agenda (two sizes), Month, Network, Display**, and GAMING. The console has no errors. The new kinds have no renderer yet, so don't place them; that's expected. Stop the preview server when you're done.

- [ ] **Step 9: Commit**

```bash
git add src/Deck.Shell tests/Deck.Shell.Tests
git commit -m "Add catalogue entries and config for the new widgets" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Countdown

**Files:**
- Create: `src/Deck.Shell/Countdowns/CountdownText.cs`, `src/Deck.Shell/Countdowns/CountdownInput.cs`, `src/Deck.Shell/Widgets/CountdownWidget.cs`, `src/Deck.Shell/CountdownWindow.xaml`, `src/Deck.Shell/CountdownWindow.xaml.cs`, `src/Deck.Shell/ui/countdown.html`, `src/Deck.Shell/ui/widgets/countdown.js`, `src/Deck.Shell/ui/widgets/countdown.css`
- Modify: `src/Deck.Shell/Widgets/WidgetFactory.cs`, `src/Deck.Shell/MainWindow.xaml.cs`, `src/Deck.Shell/ui/deck.html`, `src/Deck.Shell/ui/deck.js`, `src/Deck.Shell/ui/edit.js`, `src/Deck.Shell/ui/dev/harness.html`, `src/Deck.Shell/ui/dev/harness.js`
- Test: `tests/Deck.Shell.Tests/CountdownTextTests.cs`, `tests/Deck.Shell.Tests/CountdownInputTests.cs`

**Interfaces:**
- Consumes: `Countdown`, `DeckConfig.Countdowns`, catalogue kind `countdown` (Task 1). In `MainWindow`: `CommitLayout(DeckLayout)`, `PushLayout()`, `BuildLibrary`, `LibraryItem`, `HandleLayoutOp`, `DeleteItem`, and the `LayoutOp(Op, Kind, Variant, Ref, Col, Row)` record.
- Produces:
  - `CountdownPhase { Ahead, Soon, Reached, Past }`
  - `CountdownView(string Value, CountdownPhase Phase)`
  - `CountdownText.Describe(DateTime target, bool hasTime, DateTime now)` and `CountdownText.DateLine(DateTime target, bool hasTime)`
  - `CountdownInput.TryCreate(string? label, string? date, string? time, out Countdown countdown)`
  - Countdown posts `{label, value, phase, date}`
  - Layout ops `new-countdown` (col, row) and `edit-item` (kind, ref); `delete` now also takes `countdown`
  - Page widget-definition flag `editable: true`, which makes right-click post `edit-item`

- [ ] **Step 1: Write the failing tests**

`tests/Deck.Shell.Tests/CountdownTextTests.cs`:

```csharp
using Deck.Shell.Countdowns;

namespace Deck.Shell.Tests;

public class CountdownTextTests
{
    // A Thursday afternoon.
    private static readonly DateTime Now = new(2026, 9, 24, 14, 35, 0);

    [Theory]
    [InlineData(2026, 10, 30, "36 days", "Ahead")]
    [InlineData(2026, 9, 26, "2 days", "Ahead")]
    [InlineData(2026, 9, 25, "tomorrow", "Soon")]
    [InlineData(2026, 9, 24, "today!", "Reached")]
    [InlineData(2026, 9, 23, "+1 day", "Reached")]
    [InlineData(2026, 9, 12, "+12 days", "Past")]
    public void Date_only_countdowns_count_calendar_days(int y, int m, int d, string value, string phase)
    {
        var view = CountdownText.Describe(new DateTime(y, m, d), hasTime: false, Now);

        Assert.Equal(value, view.Value);
        Assert.Equal(phase, view.Phase.ToString());
    }

    [Theory]
    [InlineData("2026-09-27 18:00", "3 days", "Ahead")]     // 76h away
    [InlineData("2026-09-26 12:00", "45h 25m", "Soon")]     // under 48h
    [InlineData("2026-09-24 14:35:30", "1m", "Soon")]       // the last minute rounds up
    [InlineData("2026-09-24 09:23", "+5h 12m", "Reached")]  // passed today
    [InlineData("2026-09-20 10:00", "+4 days", "Past")]
    [InlineData("2026-09-23 14:35", "+1 day", "Past")]      // exactly a day ago
    public void Timed_countdowns_switch_to_hours_under_48h_and_count_up_after(string target, string value, string phase)
    {
        var view = CountdownText.Describe(DateTime.Parse(target, System.Globalization.CultureInfo.InvariantCulture), hasTime: true, Now);

        Assert.Equal(value, view.Value);
        Assert.Equal(phase, view.Phase.ToString());
    }

    [Fact]
    public void Date_line_says_what_is_being_counted_to()
    {
        Assert.Equal("Sat 12 Dec 2026 · 18:00", CountdownText.DateLine(new DateTime(2026, 12, 12, 18, 0, 0), hasTime: true));
        Assert.Equal("Sat 12 Dec 2026", CountdownText.DateLine(new DateTime(2026, 12, 12), hasTime: false));
    }
}
```

`tests/Deck.Shell.Tests/CountdownInputTests.cs`:

```csharp
using Deck.Shell.Countdowns;

namespace Deck.Shell.Tests;

public class CountdownInputTests
{
    [Fact]
    public void A_date_and_time_make_a_timed_countdown()
    {
        Assert.True(CountdownInput.TryCreate("vacation", "2026-12-12", "18:30", out var countdown));

        Assert.Equal("VACATION", countdown.Label);
        Assert.Equal(new DateTime(2026, 12, 12, 18, 30, 0), countdown.Target);
        Assert.True(countdown.HasTime);
    }

    [Fact]
    public void Without_a_time_it_counts_to_the_day()
    {
        Assert.True(CountdownInput.TryCreate("Trip", "2026-12-12", "", out var countdown));

        Assert.Equal(new DateTime(2026, 12, 12), countdown.Target);
        Assert.False(countdown.HasTime);
    }

    [Fact]
    public void A_missing_or_bad_date_is_refused()
    {
        Assert.False(CountdownInput.TryCreate("Trip", "", "10:00", out _));
        Assert.False(CountdownInput.TryCreate("Trip", "12/12/2026", null, out _));
    }

    [Fact]
    public void Names_are_tidied_to_fit_the_tile()
    {
        CountdownInput.TryCreate("   ", "2026-12-12", null, out var unnamed);
        CountdownInput.TryCreate("a very long countdown name", "2026-12-12", null, out var longName);

        Assert.Equal("COUNTDOWN", unnamed.Label);
        Assert.Equal("A VERY LONG CO", longName.Label);
    }
}
```

- [ ] **Step 2: Run them to confirm they fail**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS because `CountdownText` and `CountdownInput` don't exist.

- [ ] **Step 3: Write the countdown logic**

`src/Deck.Shell/Countdowns/CountdownText.cs`:

```csharp
using System.Globalization;

namespace Deck.Shell.Countdowns;

internal enum CountdownPhase { Ahead, Soon, Reached, Past }

internal readonly record struct CountdownView(string Value, CountdownPhase Phase);

/// <summary>
/// What a countdown tile says. The caller passes "now", so it's pure: every boundary here —
/// midnight, the 48-hour switch to hours, the day after — is the kind of thing that looks fine in
/// a quick check and is wrong on the day.
/// </summary>
internal static class CountdownText
{
    private static readonly TimeSpan HoursThreshold = TimeSpan.FromHours(48);

    public static CountdownView Describe(DateTime target, bool hasTime, DateTime now)
    {
        int days = (target.Date - now.Date).Days;

        if (!hasTime)
        {
            return days switch
            {
                >= 2 => new($"{days} days", CountdownPhase.Ahead),
                1 => new("tomorrow", CountdownPhase.Soon),
                0 => new("today!", CountdownPhase.Reached),
                -1 => new(Since(1), CountdownPhase.Reached),
                _ => new(Since(-days), CountdownPhase.Past)
            };
        }

        var left = target - now;
        if (left > TimeSpan.Zero)
        {
            return left >= HoursThreshold
                ? new($"{days} days", CountdownPhase.Ahead)
                : new(HoursMinutes(left, roundUp: true), CountdownPhase.Soon);
        }

        var since = now - target;
        return since < TimeSpan.FromDays(1)
            ? new("+" + HoursMinutes(since, roundUp: false), CountdownPhase.Reached)
            : new(Since((int)since.TotalDays), CountdownPhase.Past);
    }

    /// <summary>"Sat 12 Dec 2026 · 18:00" — the line under the value, so the tile says what it counts to.</summary>
    public static string DateLine(DateTime target, bool hasTime) =>
        target.ToString(hasTime ? "ddd d MMM yyyy · HH:mm" : "ddd d MMM yyyy", CultureInfo.InvariantCulture);

    private static string Since(int days) => days == 1 ? "+1 day" : $"+{days} days";

    /// <summary>Counting down rounds up, so the last minute reads "1m" rather than "0m"; counting up rounds down.</summary>
    private static string HoursMinutes(TimeSpan span, bool roundUp)
    {
        int minutes = (int)(roundUp ? Math.Ceiling(span.TotalMinutes) : Math.Floor(span.TotalMinutes));
        int hours = minutes / 60;
        int rest = minutes % 60;
        return hours > 0 ? $"{hours}h {rest}m" : $"{rest}m";
    }
}
```

`src/Deck.Shell/Countdowns/CountdownInput.cs`:

```csharp
using System.Globalization;

namespace Deck.Shell.Countdowns;

internal static class CountdownInput
{
    /// <summary>Longest name that still fits a tile's label line.</summary>
    public const int MaxLabel = 14;

    /// <summary>
    /// Builds a countdown from the window's form. The form already insists on a date; this is the
    /// host not trusting it. The result has a fresh id; editing copies its fields onto the
    /// existing countdown instead.
    /// </summary>
    public static bool TryCreate(string? label, string? date, string? time, out Countdown countdown)
    {
        countdown = new Countdown();

        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return false;

        bool hasTime = TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out var at);

        string name = (label ?? "").Trim().ToUpperInvariant();
        if (name.Length == 0) name = "COUNTDOWN";
        if (name.Length > MaxLabel) name = name[..MaxLabel];

        countdown.Label = name;
        countdown.Target = hasTime ? day + at : day;
        countdown.HasTime = hasTime;
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: `Passed!`, `Failed: 0`.

- [ ] **Step 5: Write the widget**

`src/Deck.Shell/Widgets/CountdownWidget.cs`:

```csharp
using Deck.Shell.Countdowns;

namespace Deck.Shell.Widgets;

/// <summary>One saved countdown. Editing and deleting are the main window's job, since they change the config's list.</summary>
internal sealed class CountdownWidget(WidgetContext context, string id) : WidgetBase(context, "countdown", id)
{
    private string _lastShown = "";

    private Countdown? Countdown => Context.Config.Countdowns.FirstOrDefault(c => c.Id == Ref);

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
        if (Countdown is not { } countdown) return;

        var view = CountdownText.Describe(countdown.Target, countdown.HasTime, DateTime.Now);
        string date = CountdownText.DateLine(countdown.Target, countdown.HasTime);
        string shown = $"{countdown.Label}|{view.Value}|{view.Phase}|{date}";

        // Ticks every second, but the text changes at most once a minute.
        if (!force && shown == _lastShown) return;
        _lastShown = shown;

        Post(new
        {
            label = countdown.Label,
            value = view.Value,
            phase = view.Phase.ToString().ToLowerInvariant(),
            date
        });
    }
}
```

In `src/Deck.Shell/Widgets/WidgetFactory.cs`, add before the `_ => null` arm:

```csharp
        { Kind: "countdown", Ref: { } countdownId } => new CountdownWidget(context, countdownId),
```

- [ ] **Step 6: Write the countdown window**

`src/Deck.Shell/CountdownWindow.xaml`:

```xml
<Window x:Class="Deck.Shell.CountdownWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
        Title="Countdown"
        Width="520" Height="380"
        ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen"
        Background="#0B0D10">

    <Grid>
        <wv2:WebView2 x:Name="Web" DefaultBackgroundColor="#0B0D10" />
    </Grid>
</Window>
```

`src/Deck.Shell/CountdownWindow.xaml.cs`:

```csharp
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using Deck.Shell.Countdowns;
using Microsoft.Web.WebView2.Core;

namespace Deck.Shell;

/// <summary>
/// Creates or edits a countdown. An ordinary focusable window, like the capture dialog: the deck
/// itself can never take keyboard input, and a name and a date both need typing.
/// </summary>
public partial class CountdownWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Countdown? _existing;

    /// <summary>The countdown as entered. It has a fresh id; when editing, the host copies its fields across.</summary>
    internal event Action<Countdown>? Saved;

    internal event Action? Deleted;

    internal CountdownWindow(Countdown? existing)
    {
        InitializeComponent();
        _existing = existing;
        Title = existing is null ? "New countdown" : "Edit countdown";
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Deck", "WebView2");

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(env);

            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.WebMessageReceived += OnWebMessage;
            Web.CoreWebView2.NavigationCompleted += (_, _) => SendState();

            string path = Path.Combine(AppContext.BaseDirectory, "ui", "countdown.html");
            Web.CoreWebView2.NavigateToString(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Countdown window failed to start");
            Close();
        }
    }

    private void SendState()
    {
        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "countdown",
            editing = _existing is not null,
            label = _existing?.Label ?? "",
            date = _existing?.Target.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            time = _existing is { HasTime: true } timed
                ? timed.Target.ToString("HH:mm", CultureInfo.InvariantCulture)
                : ""
        }));
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        switch (message)
        {
            case "cancel":
                Close();
                return;

            case "delete":
                ConfirmDelete();
                return;
        }

        const string savePrefix = "save:";
        if (!message.StartsWith(savePrefix, StringComparison.Ordinal)) return;

        SavePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SavePayload>(message[savePrefix.Length..], JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (payload is null || !CountdownInput.TryCreate(payload.Label, payload.Date, payload.Time, out var countdown))
            return;

        Saved?.Invoke(countdown);
        Close();
    }

    private void ConfirmDelete()
    {
        if (_existing is null) return;

        var answer = MessageBox.Show(this,
            $"Delete the countdown \"{_existing.Label}\"?",
            "Delete countdown", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        Deleted?.Invoke();
        Close();
    }

    private sealed record SavePayload(string? Label, string? Date, string? Time);
}
```

`src/Deck.Shell/ui/countdown.html` (the inline script must have no comments):

```html
<meta charset="utf-8" />
<title>Countdown</title>
<style>
  :root {
    --bg: #0b0d10;
    --panel: #171b21;
    --line: #2a313b;
    --text: #e6eaf0;
    --dim: #8b95a4;
    --accent: #3fb950;
    --danger: #f85149;
  }

  * { box-sizing: border-box; }

  html, body {
    margin: 0;
    height: 100%;
    background: var(--bg);
    color: var(--text);
    font: 14px/1.45 "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
  }

  body { display: grid; grid-template-rows: 1fr auto; }

  main { padding: 22px 24px 8px; display: flex; flex-direction: column; gap: 16px; }
  h1 { margin: 0; font-size: 19px; font-weight: 650; }

  label { display: flex; flex-direction: column; gap: 6px; color: var(--dim); font-size: 13px; }
  .opt { color: #5b6474; font-size: 12px; }
  .row { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }

  input {
    background: #0e1116;
    border: 1px solid var(--line);
    border-radius: 8px;
    color: var(--text);
    padding: 9px 12px;
    font: inherit;
    color-scheme: dark;
  }
  input:focus { outline: none; border-color: var(--accent); }
  #label { font-weight: 600; letter-spacing: 0.04em; text-transform: uppercase; }

  .error { color: var(--danger); font-size: 13px; min-height: 18px; }

  footer {
    padding: 14px 24px 20px;
    border-top: 1px solid var(--line);
    display: flex;
    gap: 12px;
    align-items: center;
  }
  .spacer { flex: 1; }

  button {
    border-radius: 8px;
    padding: 9px 18px;
    font: inherit;
    font-weight: 600;
    cursor: pointer;
    border: 1px solid var(--line);
    background: var(--panel);
    color: var(--text);
  }
  button.primary { background: var(--accent); border-color: var(--accent); color: #08130b; }
  button.danger { color: var(--danger); border-color: #7d2a2f; }
  button:hover { filter: brightness(1.12); }
</style>

<main>
  <h1 id="heading">New countdown</h1>
  <label>Name
    <input id="label" maxlength="14" spellcheck="false" placeholder="VACATION" />
  </label>
  <div class="row">
    <label>Date
      <input id="date" type="date" />
    </label>
    <label>Time <span class="opt">optional</span>
      <input id="time" type="time" />
    </label>
  </div>
  <div class="error" id="error"></div>
</main>

<footer>
  <button id="delete" class="danger" hidden>Delete</button>
  <span class="spacer"></span>
  <button id="cancel">Cancel</button>
  <button id="save" class="primary">Save</button>
</footer>

<script>
  const post = (m) => window.chrome.webview.postMessage(m);
  const $ = (id) => document.getElementById(id);

  window.chrome.webview.addEventListener('message', (ev) => {
    const d = ev.data;
    if (d.type !== 'countdown') return;
    $('heading').textContent = d.editing ? 'Edit countdown' : 'New countdown';
    $('label').value = d.label;
    $('date').value = d.date;
    $('time').value = d.time;
    $('delete').hidden = !d.editing;
    $('label').focus();
  });

  $('cancel').addEventListener('click', () => post('cancel'));
  $('delete').addEventListener('click', () => post('delete'));

  $('save').addEventListener('click', () => {
    if (!$('date').value) {
      $('error').textContent = 'Pick a date.';
      return;
    }
    post('save:' + JSON.stringify({ label: $('label').value, date: $('date').value, time: $('time').value }));
  });

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') $('save').click();
    if (e.key === 'Escape') post('cancel');
  });
</script>
```

- [ ] **Step 7: Wire the host**

In `src/Deck.Shell/MainWindow.xaml.cs`:

1. Add `using Deck.Shell.Countdowns;`.

2. In `BuildLibrary`, append after the shortcut loop:

```csharp
        var countdown = WidgetCatalog.Find("countdown")!;
        foreach (var c in _config.Countdowns.Where(c => !layout.IsPlaced("countdown", c.Id)))
            yield return LibraryItem(countdown, c.Id, c.Label);
```

   and update its `///` summary's last clause from "then the presets and shortcuts that aren't on it." to "then the presets, shortcuts and countdowns that aren't on it.".

3. In `HandleLayoutOp`'s `switch`, add before `case "delete" …`:

```csharp
            case "new-countdown":
                OpenCountdown(null, op.Col, op.Row);
                return;

            case "edit-item" when op is { Kind: "countdown", Ref: { } countdownId }:
                OpenCountdown(countdownId, op.Col, op.Row);
                return;
```

4. In `DeleteItem`, add a branch before the final `else { return; }`:

```csharp
        else if (kind == "countdown" && _config.Countdowns.FirstOrDefault(c => c.Id == reference) is { } countdown)
        {
            var answer = MessageBox.Show(
                $"Delete the countdown \"{countdown.Label}\"?",
                "Delete countdown", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            _config.Countdowns.Remove(countdown);
        }
```

   and change the method's summary's first sentence to "Permanently deletes a preset, shortcut or countdown, from a right-click on its tile or its library card."

5. Add this method after `OpenCapture`:

```csharp
    /// <summary>
    /// New or edit, in an ordinary window for the same reason as capture. A new countdown lands
    /// in the cell the library was opened from (or waits in the library if that cell filled up).
    /// Editing changes the saved countdown in place and re-pushes, so its tile updates at once.
    /// </summary>
    private void OpenCountdown(string? id, int col, int row)
    {
        var existing = id is null ? null : _config.Countdowns.FirstOrDefault(c => c.Id == id);
        if (id is not null && existing is null) return;

        var window = new CountdownWindow(existing);

        window.Saved += result =>
        {
            var layout = new DeckLayout(_config.Layout);

            if (existing is null)
            {
                _config.Countdowns.Add(result);
                layout.Place("countdown", WidgetCatalog.Standard, result.Id, col, row);
            }
            else
            {
                existing.Label = result.Label;
                existing.Target = result.Target;
                existing.HasTime = result.HasTime;
            }

            CommitLayout(layout);
            _host?.PushAll();
        };

        window.Deleted += () =>
        {
            if (existing is null) return;

            _config.Countdowns.Remove(existing);
            var layout = new DeckLayout(_config.Layout);
            layout.Remove("countdown", existing.Id);
            CommitLayout(layout);
        };

        window.Show();
        window.Activate();
    }
```

- [ ] **Step 8: Build and test**

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q` → `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Failed: 0`.

- [ ] **Step 9: Render the tile**

`src/Deck.Shell/ui/widgets/countdown.js` (no comments):

```js
Widgets.countdown = {
  editable: true,
  template: () => `
    <div class="label cd-label"></div>
    <div class="cd-value">–</div>
    <div class="device cd-date"></div>`,
  update(tile, d) {
    for (const phase of ['ahead', 'soon', 'reached', 'past']) tile.classList.toggle(phase, d.phase === phase);
    setText(tile, '.cd-label', d.label);
    setText(tile, '.cd-value', d.value);
    setText(tile, '.cd-date', d.date);
    tile.title = 'Right-click to edit';
  }
};
```

`src/Deck.Shell/ui/widgets/countdown.css`:

```css
/* --- countdown: amber when it's close, green on the day, dim once it's history --- */
.w-countdown .cd-value {
  font-size: 24px;
  font-weight: 650;
  line-height: 1.1;
  font-variant-numeric: tabular-nums;
}
.tile.w-countdown.soon { border-color: #6b5312; }
.w-countdown.soon .cd-value, .w-countdown.soon .cd-label { color: var(--warn); }
.tile.w-countdown.reached { background: var(--ok-bg); border-color: var(--ok-line); }
.w-countdown.reached .cd-value, .w-countdown.reached .cd-label { color: var(--ok); }
.tile.w-countdown.past { opacity: 0.6; }
```

In `src/Deck.Shell/ui/deck.html`, add `<link rel="stylesheet" href="widgets/countdown.css" />` directly after the `deck.css` link, and `<script defer src="widgets/countdown.js"></script>` directly after the `widgets.js` script. In `src/Deck.Shell/ui/dev/harness.html`, add the same two lines with the `../` prefix (`../widgets/countdown.css`, `../widgets/countdown.js`) in the same positions.

In `src/Deck.Shell/ui/deck.js`, in `buildTile`'s `contextmenu` handler, replace:

```js
    if (def.deletable) layoutOp({ op: 'delete', kind: p.kind, ref: p.ref });
```

with:

```js
    if (def.editable) layoutOp({ op: 'edit-item', kind: p.kind, ref: p.ref });
    else if (def.deletable) layoutOp({ op: 'delete', kind: p.kind, ref: p.ref });
```

In `src/Deck.Shell/ui/edit.js`, in `renderLibrary`, directly after the `New preset` card's `cards.append(…);` statement, add:

```js
    cards.append(card('New countdown', 'count down to a date', false, () => {
      closeLibrary();
      layoutOp({ op: 'new-countdown', col, row });
    }, null));
```

- [ ] **Step 10: Teach the harness about countdowns**

In `src/Deck.Shell/ui/dev/harness.js`:

- After the `const presets = …;` line, add:

```js
  const countdowns = [{ id: 'c1', label: 'VACATION', value: '42 days', phase: 'ahead', date: 'Fri 6 Nov 2026' }];
```

- In `sendLayout`, replace `library: builtIns.concat(items)` with:

```js
      library: builtIns.concat(items, countdowns.filter((c) => !placed('countdown', c.id)).map((c) => ({
        kind: 'countdown', ref: c.id, title: c.label, group: 'Countdown',
        variants: [{ variant: 'standard', label: '1×1', w: 1, h: 1 }]
      })))
```

- At the end of `sendData`'s body, add:

```js
    for (const c of countdowns) {
      emit({ type: 'widget', kind: 'countdown', ref: c.id, data: { label: c.label, value: c.value, phase: c.phase, date: c.date } });
    }
```

- In `handleLayout`, add a branch before `} else if (op.op === 'delete') {`:

```js
    } else if (op.op === 'new-countdown') {
      const id = 'c' + (countdowns.length + 1);
      countdowns.push({ id, label: 'NEW', value: 'tomorrow', phase: 'soon', date: 'Fri 25 Sep 2026' });
      if (fits('countdown', 'standard', op.col, op.row, null)) {
        placements.push({ kind: 'countdown', variant: 'standard', ref: id, col: op.col, row: op.row });
      }
      sendData();
```

- In the `delete` branch, after the `presets.splice` line, add:

```js
      const cdIndex = countdowns.findIndex((c) => c.id === op.ref);
      if (cdIndex >= 0) countdowns.splice(cdIndex, 1);
```

- [ ] **Step 11: Check it in the harness**

Start `deck-ui`, open the harness at 1077×519, enter edit mode (click an empty cell), and click a `+`:
1. The library shows **New countdown** after New preset, plus a **VACATION** card (group "Countdown").
2. Click New countdown. A **NEW** tile appears at that cell, amber ("tomorrow" / "Fri 25 Sep 2026").
3. Add VACATION from the library into another free cell. It shows "42 days" in normal colours.
4. Click Done, then right-click the VACATION tile. `harness.log.at(-1)` should be `layout:{"op":"edit-item","kind":"countdown","ref":"c1",…}`.
5. In edit mode, right-click the VACATION card in the library. The last log entry should be a `delete` op for `countdown`/`c1`.
6. The console has no errors. Take a screenshot showing both countdown tiles. Stop the server.

- [ ] **Step 12: Commit**

```bash
git add src/Deck.Shell tests/Deck.Shell.Tests
git commit -m "Add countdown widgets with a create and edit window" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Dice

**Files:**
- Create: `src/Deck.Shell/Widgets/DiceWidget.cs`, `src/Deck.Shell/ui/widgets/dice.js`, `src/Deck.Shell/ui/widgets/dice.css`
- Modify: `src/Deck.Shell/Widgets/WidgetFactory.cs`, `src/Deck.Shell/ui/deck.html`, `src/Deck.Shell/ui/dev/harness.html`, `src/Deck.Shell/ui/dev/harness.js`
- Test: `tests/Deck.Shell.Tests/DiceTests.cs`

**Interfaces:**
- Consumes: `DeckConfig.DiceMode` (Task 1). Page helpers `q`, `setText`, `Widgets`.
- Produces:
  - `DiceWidget.Modes`
  - `DiceWidget.IsResult(string mode, string value)`
  - Handles `mode` and `rolled:<value>`
  - Posts `{mode, last}`, where `last` is null or `"1"`–`"20"` / `"heads"` / `"tails"`

- [ ] **Step 1: Write the failing test**

`tests/Deck.Shell.Tests/DiceTests.cs`:

```csharp
using Deck.Shell.Widgets;

namespace Deck.Shell.Tests;

public class DiceTests
{
    [Theory]
    [InlineData("d6", "1", true)]
    [InlineData("d6", "6", true)]
    [InlineData("d6", "7", false)]
    [InlineData("d6", "0", false)]
    [InlineData("d20", "20", true)]
    [InlineData("d20", "21", false)]
    [InlineData("coin", "heads", true)]
    [InlineData("coin", "tails", true)]
    [InlineData("coin", "3", false)]
    [InlineData("d6", "heads", false)]
    public void Only_results_the_mode_could_produce_are_kept(string mode, string value, bool valid) =>
        Assert.Equal(valid, DiceWidget.IsResult(mode, value));
}
```

- [ ] **Step 2: Run it to confirm it fails**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS because `DiceWidget` doesn't exist.

- [ ] **Step 3: Write the widget**

`src/Deck.Shell/Widgets/DiceWidget.cs`:

```csharp
namespace Deck.Shell.Widgets;

/// <summary>
/// The roll happens in the page, so the tumble starts on the click with no round trip. The host
/// only keeps the mode (saved) and the last result (for this session), so a layout change
/// doesn't wipe the face.
/// </summary>
internal sealed class DiceWidget(WidgetContext context) : WidgetBase(context, "dice")
{
    public static readonly string[] Modes = ["d6", "d20", "coin"];

    private const string RolledPrefix = "rolled:";

    private string? _last;

    private string Mode => Modes.Contains(Context.Config.DiceMode) ? Context.Config.DiceMode : Modes[0];

    public override bool Handle(string message)
    {
        if (message == "mode")
        {
            Context.Config.DiceMode = Modes[(Array.IndexOf(Modes, Mode) + 1) % Modes.Length];
            Context.Config.Save();
            _last = null;
            Push();
            return true;
        }

        if (message.StartsWith(RolledPrefix, StringComparison.Ordinal))
        {
            string value = message[RolledPrefix.Length..];
            if (IsResult(Mode, value)) _last = value;
            // Echoed back so the page's cached copy matches what the face now shows.
            Push();
            return true;
        }

        return false;
    }

    public override void Push() => Post(new { mode = Mode, last = _last });

    /// <summary>Only a result the current mode could produce is remembered.</summary>
    public static bool IsResult(string mode, string value) => mode switch
    {
        "coin" => value is "heads" or "tails",
        "d20" => int.TryParse(value, out int d20) && d20 is >= 1 and <= 20,
        _ => int.TryParse(value, out int d6) && d6 is >= 1 and <= 6
    };
}
```

In `src/Deck.Shell/Widgets/WidgetFactory.cs`, add before the `preset` arm:

```csharp
        { Kind: "dice" } => new DiceWidget(context),
```

- [ ] **Step 4: Run the tests and build**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Failed: 0`.
Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q` → `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 5: Render the die**

`src/Deck.Shell/ui/widgets/dice.js` (no comments):

```js
const DICE_PIPS = { 1: [4], 2: [0, 8], 3: [0, 4, 8], 4: [0, 2, 6, 8], 5: [0, 2, 4, 6, 8], 6: [0, 2, 3, 5, 6, 8] };
const DICE_LABELS = { d6: 'D6', d20: 'D20', coin: 'COIN' };

function diceRandom(max) {
  const buf = new Uint32Array(1);
  const limit = Math.floor(0x100000000 / max) * max;
  do {
    crypto.getRandomValues(buf);
  } while (buf[0] >= limit);
  return (buf[0] % max) + 1;
}

function diceValue(mode) {
  if (mode === 'coin') return diceRandom(2) === 1 ? 'heads' : 'tails';
  return String(diceRandom(mode === 'd20' ? 20 : 6));
}

function drawDiceFace(tile, mode, value) {
  const face = q(tile, '.dice-face');
  face.innerHTML = '';
  face.className = 'dice-face ' + (value == null ? 'empty' : mode);
  if (value == null) {
    face.textContent = '?';
    return;
  }
  if (mode === 'd6') {
    const on = new Set(DICE_PIPS[value] || []);
    for (let i = 0; i < 9; i++) {
      const pip = document.createElement('span');
      pip.className = on.has(i) ? 'pip' : 'pip off';
      face.append(pip);
    }
  } else if (mode === 'coin') {
    face.textContent = value === 'heads' ? 'H' : 'T';
  } else {
    face.textContent = value;
  }
}

function showDiceResult(tile, mode, value) {
  drawDiceFace(tile, mode, value);
  setText(tile, '.dice-result',
    value == null ? 'click to roll'
    : mode === 'coin' ? (value === 'heads' ? 'Heads' : 'Tails')
    : '');
}

function rollDice(tile, send) {
  const state = tile.dice;
  if (state.rolling) return;
  state.rolling = true;
  tile.classList.add('rolling');
  setText(tile, '.dice-result', '');
  const flicker = setInterval(() => drawDiceFace(tile, state.mode, diceValue(state.mode)), 70);
  setTimeout(() => {
    clearInterval(flicker);
    const value = diceValue(state.mode);
    state.rolling = false;
    tile.classList.remove('rolling');
    showDiceResult(tile, state.mode, value);
    send('rolled:' + value);
  }, 900);
}

Widgets.dice = {
  context: 'mode',
  template: () => `
    <div class="dice-hit"></div>
    <div class="label dice-mode">D6</div>
    <div class="dice-face empty">?</div>
    <div class="sub dice-result">click to roll</div>`,
  bind(tile, send) {
    tile.dice = { mode: 'd6', rolling: false };
    q(tile, '.dice-hit').addEventListener('click', () => rollDice(tile, send));
  },
  update(tile, d) {
    tile.dice.mode = d.mode;
    setText(tile, '.dice-mode', DICE_LABELS[d.mode] || 'D6');
    if (!tile.dice.rolling) showDiceResult(tile, d.mode, d.last);
    tile.title = 'Click to roll · right-click to switch between d6, d20 and a coin';
  }
};
```

`src/Deck.Shell/ui/widgets/dice.css`:

```css
/* --- dice: the whole tile is the button, so a hit layer fills it; the edit shield (z-index 2)
   still covers it in edit mode --- */
.w-dice .dice-hit { position: absolute; inset: 0; z-index: 1; cursor: pointer; }
.tile.w-dice:hover { background: var(--tile-hi); }
.tile.w-dice:active .dice-face { transform: scale(0.95); }

.w-dice .dice-face {
  width: 62px;
  height: 62px;
  display: grid;
  place-items: center;
  font-size: 26px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: #0b0d10;
  background: var(--text);
  border-radius: 12px;
  box-shadow: inset 0 -3px 0 rgba(0, 0, 0, 0.18);
}
.w-dice .dice-face.empty { background: #0e1116; color: var(--dim); border: 1px dashed var(--line); box-shadow: none; }
.w-dice .dice-face.d6 { grid-template-columns: repeat(3, 1fr); grid-template-rows: repeat(3, 1fr); padding: 9px; gap: 2px; }
.w-dice .pip { width: 11px; height: 11px; border-radius: 50%; background: #0b0d10; }
.w-dice .pip.off { visibility: hidden; }
.w-dice .dice-face.d20 {
  border-radius: 0;
  background: var(--preset-text);
  clip-path: polygon(50% 0, 95% 25%, 95% 75%, 50% 100%, 5% 75%, 5% 25%);
}
.w-dice .dice-face.coin { border-radius: 50%; background: var(--warn); box-shadow: inset 0 0 0 4px rgba(0, 0, 0, 0.15); }

.w-dice.rolling .dice-face { animation: diceTumble 0.9s cubic-bezier(0.2, 0.7, 0.3, 1); }

@keyframes diceTumble {
  0% { transform: rotate(0deg) scale(1); }
  30% { transform: rotate(200deg) scale(0.8) translateY(-6px); }
  70% { transform: rotate(320deg) scale(1.05); }
  100% { transform: rotate(360deg) scale(1); }
}
```

In `deck.html`, add `<link rel="stylesheet" href="widgets/dice.css" />` after the last `widgets/…css` link and `<script defer src="widgets/dice.js"></script>` after the last `widgets/…js` script. In `harness.html`, do the same with `../widgets/`.

In `harness.js`, add to `SAMPLE`, after the `stopwatch` entry (with a comma after that entry):

```js
    dice: { mode: 'd6', last: '5' }
```

- [ ] **Step 6: Check it in the harness**

Start `deck-ui` and open the harness at 1077×519. Place Dice from the library into a free cell, then click Done.
1. The tile shows "D6" and a white die with five pips.
2. Click it. The die tumbles for about a second with its face flickering, then settles on a value. `harness.log.at(-1)` should start with `widget:{"kind":"dice","ref":null,"msg":"rolled:`.
3. Right-click it. The last log entry should be `widget:{"kind":"dice","ref":null,"msg":"mode"}`. The harness doesn't answer that message, so the tile doesn't change.
4. To check the other faces, run `harness.emit({type:'widget',kind:'dice',ref:null,data:{mode:'d20',last:'17'}})` and screenshot a blue hexagon showing 17. Then run `…data:{mode:'coin',last:'heads'}` and screenshot an amber circle showing "H" with "Heads" below.
5. In edit mode, clicking the die does not roll it.
6. The console has no errors. Stop the server.

- [ ] **Step 7: Commit**

```bash
git add src/Deck.Shell tests/Deck.Shell.Tests
git commit -m "Add the dice widget" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Network

**Files:**
- Create: `src/Deck.Shell/Network/NetworkHealth.cs`, `src/Deck.Shell/Network/NetworkCounters.cs`, `src/Deck.Shell/Widgets/NetworkWidget.cs`, `src/Deck.Shell/ui/widgets/network.js`, `src/Deck.Shell/ui/widgets/network.css`
- Modify: `src/Deck.Shell/Widgets/WidgetFactory.cs`, `src/Deck.Shell/ui/deck.html`, `src/Deck.Shell/ui/dev/harness.html`, `src/Deck.Shell/ui/dev/harness.js`
- Test: `tests/Deck.Shell.Tests/NetworkHealthTests.cs`

**Interfaces:**
- Consumes: `TickService` via `Context.Tick`.
- Produces:
  - `NetworkState { Good, Slow, Bad, Offline }`
  - `NetworkHealth.Assess(bool adapterUp, long? lastPingMs, int consecutiveFailures)`
  - `NetworkHealth.Rate(double bytesPerSecond)`
  - `NetworkCounters.Read()` returns `(bool AnyUp, long Received, long Sent)`
  - Posts `{state, ping, down, up}`

- [ ] **Step 1: Write the failing tests**

`tests/Deck.Shell.Tests/NetworkHealthTests.cs`:

```csharp
using Deck.Shell.Network;

namespace Deck.Shell.Tests;

public class NetworkHealthTests
{
    [Theory]
    [InlineData(false, 18, 0, "Offline")]
    [InlineData(true, 18, 0, "Good")]
    [InlineData(true, 119, 0, "Good")]
    [InlineData(true, 120, 0, "Slow")]
    [InlineData(true, 18, 1, "Slow")]
    [InlineData(true, 18, 2, "Bad")]
    [InlineData(true, -1, 0, "Good")]      // -1 = no ping has come back yet
    [InlineData(false, -1, 5, "Offline")]  // no adapter beats lost pings
    public void Assess(bool up, int pingMs, int failures, string expected) =>
        Assert.Equal(expected, NetworkHealth.Assess(up, pingMs < 0 ? null : pingMs, failures).ToString());

    [Theory]
    [InlineData(0, "0 kb/s")]
    [InlineData(105_000, "840 kb/s")]
    [InlineData(1_550_000, "12.4 Mb/s")]
    [InlineData(127_500_000, "1.02 Gb/s")]
    [InlineData(-5, "0 kb/s")]
    public void Rates_are_shown_in_bits_like_connections_are_sold(double bytesPerSecond, string expected) =>
        Assert.Equal(expected, NetworkHealth.Rate(bytesPerSecond));
}
```

- [ ] **Step 2: Run them to confirm they fail**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS because `Deck.Shell.Network` doesn't exist.

- [ ] **Step 3: Write the network logic**

`src/Deck.Shell/Network/NetworkHealth.cs`:

```csharp
using System.Globalization;

namespace Deck.Shell.Network;

internal enum NetworkState { Good, Slow, Bad, Offline }

/// <summary>Turns raw readings into what the tile says. Pure, so the thresholds are tested rather than eyeballed.</summary>
internal static class NetworkHealth
{
    /// <summary>Above this a call starts to feel it; below it nobody notices.</summary>
    public const int SlowPingMs = 120;

    /// <param name="lastPingMs">The most recent successful ping, or null if none has come back yet.</param>
    /// <param name="consecutiveFailures">
    /// Lost pings in a row. One is a blip worth an amber hint; two is a connection in trouble.
    /// </param>
    public static NetworkState Assess(bool adapterUp, long? lastPingMs, int consecutiveFailures)
    {
        if (!adapterUp) return NetworkState.Offline;
        if (consecutiveFailures >= 2) return NetworkState.Bad;
        if (consecutiveFailures == 1 || lastPingMs >= SlowPingMs) return NetworkState.Slow;
        return NetworkState.Good;
    }

    /// <summary>Bytes per second shown as bits, the unit connections are sold in: "840 kb/s", "12.4 Mb/s", "1.02 Gb/s".</summary>
    public static string Rate(double bytesPerSecond)
    {
        double bits = Math.Max(0, bytesPerSecond) * 8;

        return bits switch
        {
            < 1_000_000 => (bits / 1_000).ToString("0", CultureInfo.InvariantCulture) + " kb/s",
            < 1_000_000_000 => (bits / 1_000_000).ToString("0.0", CultureInfo.InvariantCulture) + " Mb/s",
            _ => (bits / 1_000_000_000).ToString("0.00", CultureInfo.InvariantCulture) + " Gb/s"
        };
    }
}
```

`src/Deck.Shell/Network/NetworkCounters.cs`:

```csharp
using System.Net;
using System.Net.NetworkInformation;

namespace Deck.Shell.Network;

/// <summary>
/// Total bytes through the adapters that reach the internet. Only adapters with a default gateway
/// count: virtual switches (Hyper-V, WSL) carry the same traffic again and would double every
/// number.
/// </summary>
internal static class NetworkCounters
{
    public static (bool AnyUp, long Received, long Sent) Read()
    {
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return (false, 0, 0);
        }

        bool up = false;
        long received = 0;
        long sent = 0;

        foreach (var adapter in adapters)
        {
            try
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                bool hasGateway = adapter.GetIPProperties().GatewayAddresses
                    .Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                if (!hasGateway) continue;

                var stats = adapter.GetIPStatistics();
                up = true;
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
                // An adapter vanishing mid-read (a USB dongle, a VPN dropping) — skip it.
            }
        }

        return (up, received, sent);
    }
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Failed: 0`.

- [ ] **Step 5: Write the widget**

`src/Deck.Shell/Widgets/NetworkWidget.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Deck.Shell.Network;

namespace Deck.Shell.Widgets;

/// <summary>
/// Ping and live throughput. The ping is the only traffic this creates — the speeds come from the
/// adapters' own counters, so the tile never competes with a call for bandwidth.
/// </summary>
internal sealed class NetworkWidget(WidgetContext context) : WidgetBase(context, "network")
{
    /// <summary>Cloudflare's resolver, by address: no DNS lookup to fail first and muddy the answer.</summary>
    private static readonly IPAddress Target = IPAddress.Parse("1.1.1.1");

    private const int PingTimeoutMs = 1000;

    private readonly Stopwatch _sinceLastRead = new();
    private Ping? _ping;
    private bool _pinging;
    private int _ticks;
    private long? _lastPingMs;
    private int _failures;
    private (bool AnyUp, long Received, long Sent) _last;
    private double _down;
    private double _up;

    public override void Start()
    {
        _ping = new Ping();
        _last = NetworkCounters.Read();
        _sinceLastRead.Restart();
        Context.Tick.Ticked += OnTick;
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        Context.Tick.Ticked -= OnTick;
        Context.Tick.Release();
        _ping?.Dispose();
        _ping = null;
    }

    public override void Push()
    {
        var state = NetworkHealth.Assess(_last.AnyUp, _lastPingMs, _failures);

        Post(new
        {
            state = state.ToString().ToLowerInvariant(),
            ping = state switch
            {
                NetworkState.Offline => "offline",
                NetworkState.Bad => "no reply",
                _ => _lastPingMs is { } ms ? $"{ms} ms" : "–"
            },
            down = NetworkHealth.Rate(_down),
            up = NetworkHealth.Rate(_up)
        });
    }

    private void OnTick()
    {
        var now = NetworkCounters.Read();
        double seconds = _sinceLastRead.Elapsed.TotalSeconds;
        _sinceLastRead.Restart();

        if (seconds > 0)
        {
            // Counters restart when an adapter comes back; a negative step is that, not traffic.
            _down = Math.Max(0, now.Received - _last.Received) / seconds;
            _up = Math.Max(0, now.Sent - _last.Sent) / seconds;
        }

        _last = now;

        // Every other second: often enough to catch a drop, rare enough to be no load at all.
        if (_ticks++ % 2 == 0) _ = PingAsync();

        Push();
    }

    private async Task PingAsync()
    {
        if (_pinging || _ping is not { } ping) return;

        _pinging = true;
        try
        {
            var reply = await ping.SendPingAsync(Target, PingTimeoutMs);
            if (reply.Status == IPStatus.Success)
            {
                _lastPingMs = reply.RoundtripTime;
                _failures = 0;
            }
            else
            {
                _failures++;
            }
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException or ObjectDisposedException)
        {
            // No route, or the tile was removed mid-ping and disposed it.
            _failures++;
        }
        finally
        {
            _pinging = false;
        }
    }
}
```

In `WidgetFactory.cs`, add before the `preset` arm:

```csharp
        { Kind: "network" } => new NetworkWidget(context),
```

- [ ] **Step 6: Render the tile**

`src/Deck.Shell/ui/widgets/network.js` (no comments):

```js
Widgets.network = {
  template: () => `
    <div class="label">NETWORK</div>
    <div class="net-ping">–</div>
    <div class="net-rates"><span class="net-down">↓ –</span><span class="net-up">↑ –</span></div>`,
  update(tile, d) {
    for (const s of ['good', 'slow', 'bad', 'offline']) tile.classList.toggle(s, d.state === s);
    setText(tile, '.net-ping', d.ping);
    setText(tile, '.net-down', '↓ ' + d.down);
    setText(tile, '.net-up', '↑ ' + d.up);
  }
};
```

`src/Deck.Shell/ui/widgets/network.css`:

```css
/* --- network: read-only, so no .pressable. The ping carries the colour; the speeds stay quiet --- */
.w-network .net-ping { font-size: 24px; font-weight: 650; font-variant-numeric: tabular-nums; }
.w-network .net-rates {
  display: flex;
  gap: 12px;
  font-size: 11.5px;
  color: var(--dim);
  font-variant-numeric: tabular-nums;
}
.w-network.good .net-ping { color: var(--ok); }
.tile.w-network.slow { border-color: #6b5312; }
.w-network.slow .net-ping { color: var(--warn); }
.tile.w-network.bad, .tile.w-network.offline { background: var(--danger-bg); border-color: var(--danger-line); }
.w-network.bad .net-ping, .w-network.offline .net-ping,
.w-network.bad .label, .w-network.offline .label { color: var(--danger); }
```

Add the css link and js script to `deck.html` and `harness.html` as in Task 3. In `harness.js` `SAMPLE`, after the `dice` entry (add a comma after it):

```js
    network: { state: 'good', ping: '18 ms', down: '12.4 Mb/s', up: '0.8 Mb/s' }
```

- [ ] **Step 7: Build, test, check**

Run the build (`0 Warning(s)`, `0 Error(s)`) and the tests (`Failed: 0`).
In the harness at 1077×519, place Network and screenshot it: "NETWORK", a green "18 ms", and "↓ 12.4 Mb/s ↑ 0.8 Mb/s". Then run `harness.emit({type:'widget',kind:'network',ref:null,data:{state:'bad',ping:'no reply',down:'0 kb/s',up:'0 kb/s'}})` and screenshot the tile turning red with "no reply". The console has no errors. Stop the server.

- [ ] **Step 8: Commit**

```bash
git add src/Deck.Shell tests/Deck.Shell.Tests
git commit -m "Add the network widget" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Display — brightness and warm tint

**Files:**
- Create: `src/Deck.Shell/Interop/DisplayNative.cs`, `src/Deck.Shell/Display/MonitorBrightness.cs`, `src/Deck.Shell/Display/BrightnessShift.cs`, `src/Deck.Shell/Display/GammaTint.cs`, `src/Deck.Shell/Widgets/DisplayWidget.cs`, `src/Deck.Shell/ui/widgets/display.js`, `src/Deck.Shell/ui/widgets/display.css`
- Modify: `src/Deck.Shell/Widgets/WidgetFactory.cs`, `src/Deck.Shell/App.xaml.cs`, `src/Deck.Shell/ui/deck.html`, `src/Deck.Shell/ui/dev/harness.html`, `src/Deck.Shell/ui/dev/harness.js`
- Test: `tests/Deck.Shell.Tests/BrightnessShiftTests.cs`, `tests/Deck.Shell.Tests/GammaTintTests.cs`

**Interfaces:**
- Consumes: `DeckConfig.DisplayTint` (Task 1), `Context.Tick`. Page helpers `horizontalDrag`, `q`, `setText`.
- Produces:
  - `MonitorLevel(string Name, bool Supported, int Percent)`
  - `MonitorBrightness` with `Read()`, `Set(IReadOnlyList<int>)` and `Dispose()`
  - `BrightnessShift.Average(IEnumerable<int>)` and `BrightnessShift.Apply(IReadOnlyList<int> start, int target)`
  - `GammaTint.Apply()`, `GammaTint.Reset()`, `GammaTint.BuildRamp(double, double, double)`, `GammaTint.IsApplied`
  - Handles `press`, `brightness:<0-100>` and `brightness-commit`
  - Posts `{ready, tint, level, monitors, unsupported[]}`

- [ ] **Step 1: Write the failing tests**

`tests/Deck.Shell.Tests/BrightnessShiftTests.cs`:

```csharp
using Deck.Shell.Display;

namespace Deck.Shell.Tests;

public class BrightnessShiftTests
{
    [Fact]
    public void Monitors_move_together_and_keep_their_differences()
    {
        // The user's desk: 7%, 100%, 6% — average 38.
        Assert.Equal(new[] { 2, 95, 1 }, BrightnessShift.Apply(new[] { 7, 100, 6 }, target: 33));
    }

    [Fact]
    public void Each_monitor_stops_at_0_and_100_on_its_own()
    {
        Assert.Equal(new[] { 0, 62, 0 }, BrightnessShift.Apply(new[] { 7, 100, 6 }, target: 0));
        Assert.Equal(new[] { 69, 100, 68 }, BrightnessShift.Apply(new[] { 7, 100, 6 }, target: 100));
    }

    [Fact]
    public void Average_rounds_and_handles_no_monitors()
    {
        Assert.Equal(38, BrightnessShift.Average(new[] { 7, 100, 6 }));
        Assert.Equal(0, BrightnessShift.Average(Array.Empty<int>()));
        Assert.Empty(BrightnessShift.Apply(Array.Empty<int>(), 50));
    }
}
```

`tests/Deck.Shell.Tests/GammaTintTests.cs`:

```csharp
using Deck.Shell.Display;

namespace Deck.Shell.Tests;

public class GammaTintTests
{
    [Fact]
    public void A_neutral_ramp_is_the_identity_windows_reports()
    {
        var ramp = GammaTint.BuildRamp(1, 1, 1);

        Assert.Equal(768, ramp.Length);
        Assert.Equal(32896, ramp[128]);        // red, mid-grey
        Assert.Equal(32896, ramp[256 + 128]);  // green
        Assert.Equal(65535, ramp[512 + 255]);  // blue, white
    }

    [Fact]
    public void The_warm_ramp_keeps_red_and_pulls_down_green_and_blue()
    {
        var ramp = GammaTint.BuildRamp(1.0, GammaTint.Green, GammaTint.Blue);

        Assert.Equal(65535, ramp[255]);
        Assert.Equal(55705, ramp[256 + 255]);  // 65535 × 0.85
        Assert.Equal(42598, ramp[512 + 255]);  // 65535 × 0.65
    }
}
```

- [ ] **Step 2: Run them to confirm they fail**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS because `Deck.Shell.Display` doesn't exist.

- [ ] **Step 3: Write the native layer**

`src/Deck.Shell/Interop/DisplayNative.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Deck.Shell.Interop;

/// <summary>Monitor brightness over DDC/CI (dxva2) and per-display gamma ramps (gdi32).</summary>
internal static class DisplayNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    public const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

    [DllImport("dxva2.dll")]
    public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE info, uint flags);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateDC(string? driver, string device, string? output, IntPtr mode);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern bool SetDeviceGammaRamp(IntPtr dc, ushort[] ramp);
}
```

- [ ] **Step 4: Write the display logic**

`src/Deck.Shell/Display/BrightnessShift.cs`:

```csharp
namespace Deck.Shell.Display;

/// <summary>
/// "Shift together": every monitor moves by the same amount from where it was when the drag
/// began, so a deliberately dimmer monitor stays dimmer. Working from the drag's starting levels,
/// not the last applied ones, means that hitting 0 or 100 mid-drag doesn't flatten the
/// differences — dragging back restores them.
/// </summary>
internal static class BrightnessShift
{
    public static int Average(IEnumerable<int> levels)
    {
        var list = levels.ToList();
        return list.Count == 0 ? 0 : (int)Math.Round(list.Average());
    }

    public static int[] Apply(IReadOnlyList<int> start, int target)
    {
        int delta = target - Average(start);
        return start.Select(level => Math.Clamp(level + delta, 0, 100)).ToArray();
    }
}
```

`src/Deck.Shell/Display/GammaTint.cs`:

```csharp
using System.Runtime.InteropServices;
using static Deck.Shell.Interop.DisplayNative;

namespace Deck.Shell.Display;

/// <summary>
/// The warm reading tint, applied through each display's gamma ramp. Fixed strength, and well
/// inside what Windows accepted on this desk's displays (it refuses ramps too far from neutral).
///
/// It only undoes what it did: <see cref="Reset"/> does nothing unless the deck applied the tint,
/// so it never overwrites another app's calibration.
/// </summary>
internal static class GammaTint
{
    public const double Green = 0.85;
    public const double Blue = 0.65;

    public static bool IsApplied { get; private set; }

    public static void Apply()
    {
        if (SetAll(BuildRamp(1.0, Green, Blue))) IsApplied = true;
    }

    public static void Reset()
    {
        if (!IsApplied) return;

        SetAll(BuildRamp(1.0, 1.0, 1.0));
        IsApplied = false;
    }

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

    /// <summary>Per display, not the whole-screen DC: on a multi-monitor desk only per-display ramps take.</summary>
    private static bool SetAll(ushort[] ramp)
    {
        bool any = false;

        for (uint i = 0; ; i++)
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            IntPtr dc = CreateDC(null, device.DeviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) continue;

            try
            {
                any |= SetDeviceGammaRamp(dc, ramp);
            }
            finally
            {
                DeleteDC(dc);
            }
        }

        return any;
    }
}
```

`src/Deck.Shell/Display/MonitorBrightness.cs`:

```csharp
using static Deck.Shell.Interop.DisplayNative;

namespace Deck.Shell.Display;

internal sealed record MonitorLevel(string Name, bool Supported, int Percent);

/// <summary>
/// Brightness over DDC/CI: the monitor's own setting, changed down the cable, rather than a
/// software dimmer. Every call is a slow round trip to the monitor's controller (tens of
/// milliseconds), so callers keep this off the UI thread. A lock serialises the calls, since
/// monitors don't like overlapping requests.
/// </summary>
internal sealed class MonitorBrightness : IDisposable
{
    private sealed record Monitor(string Name, IntPtr Handle, uint Min, uint Max, bool Supported);

    private readonly object _gate = new();
    private readonly List<PHYSICAL_MONITOR[]> _groups = [];
    private readonly List<Monitor> _monitors = [];
    private bool _disposed;

    public MonitorBrightness()
    {
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0) return true;

            var group = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, group)) return true;
            _groups.Add(group);

            foreach (var physical in group)
            {
                bool supported = GetMonitorBrightness(physical.hPhysicalMonitor, out uint min, out _, out uint max) && max > min;
                _monitors.Add(new Monitor(physical.szPhysicalMonitorDescription, physical.hPhysicalMonitor, min, max, supported));
            }

            return true;
        }, IntPtr.Zero);
    }

    /// <summary>Every monitor, in a fixed order; unsupported ones report Percent 0.</summary>
    public IReadOnlyList<MonitorLevel> Read()
    {
        lock (_gate)
        {
            return _monitors.Select(m =>
            {
                if (_disposed || !m.Supported || !GetMonitorBrightness(m.Handle, out _, out uint current, out _))
                    return new MonitorLevel(m.Name, false, 0);

                return new MonitorLevel(m.Name, true, ToPercent(current, m.Min, m.Max));
            }).ToList();
        }
    }

    /// <summary>One percentage per monitor, in <see cref="Read"/>'s order. Unsupported monitors are skipped.</summary>
    public void Set(IReadOnlyList<int> percents)
    {
        lock (_gate)
        {
            if (_disposed) return;

            for (int i = 0; i < _monitors.Count && i < percents.Count; i++)
            {
                var m = _monitors[i];
                if (m.Supported) SetMonitorBrightness(m.Handle, FromPercent(percents[i], m.Min, m.Max));
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var group in _groups) DestroyPhysicalMonitors((uint)group.Length, group);
        }
    }

    private static int ToPercent(uint value, uint min, uint max) =>
        (int)Math.Round((value - min) * 100.0 / (max - min));

    private static uint FromPercent(int percent, uint min, uint max) =>
        min + (uint)Math.Round(Math.Clamp(percent, 0, 100) * (max - min) / 100.0);
}
```

- [ ] **Step 5: Run the tests to confirm they pass**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Failed: 0`.

- [ ] **Step 6: Write the widget**

`src/Deck.Shell/Widgets/DisplayWidget.cs`:

```csharp
using System.Globalization;
using Deck.Shell.Display;

namespace Deck.Shell.Widgets;

/// <summary>
/// Brightness for every DDC/CI monitor, shifted together, plus the warm reading tint. The monitor
/// calls are slow, so they run on a background task that only ever applies the newest requested
/// levels — a drag never builds up a backlog of stale ones.
/// </summary>
internal sealed class DisplayWidget(WidgetContext context) : WidgetBase(context, "display")
{
    private const string BrightnessPrefix = "brightness:";

    /// <summary>How often a drag reaches the monitors. Faster just queues work inside their controllers.</summary>
    private static readonly TimeSpan ApplyInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Sleep, a display change or a game can reset gamma; putting the tint back every few seconds is cheaper than watching for all of them.</summary>
    private const int TintRefreshTicks = 10;

    private readonly object _gate = new();
    private MonitorBrightness? _monitors;
    private IReadOnlyList<MonitorLevel> _levels = [];

    /// <summary>Supported monitors' levels when the current drag began; null between drags.</summary>
    private int[]? _dragStart;

    /// <summary>The newest levels waiting to be applied, one per monitor.</summary>
    private int[]? _pending;

    private bool _applying;
    private bool _ready;
    private bool _stopped;
    private int _ticks;

    public override void Start()
    {
        Context.Tick.Ticked += OnTick;
        Context.Tick.Acquire();

        if (Context.Config.DisplayTint) GammaTint.Apply();

        _ = OpenMonitorsAsync();
    }

    public override void Stop()
    {
        _stopped = true;
        Context.Tick.Ticked -= OnTick;
        Context.Tick.Release();

        // "Removed means off": the tint goes with the tile.
        GammaTint.Reset();

        lock (_gate)
        {
            // A brightness change still in flight disposes the monitors itself when it finishes.
            if (_applying) return;
            _monitors?.Dispose();
            _monitors = null;
        }
    }

    public override bool Handle(string message)
    {
        if (message == "press")
        {
            ToggleTint();
            return true;
        }

        if (message == "brightness-commit")
        {
            _dragStart = null;
            _ = RefreshLevelsAsync();
            return true;
        }

        if (message.StartsWith(BrightnessPrefix, StringComparison.Ordinal) &&
            int.TryParse(message[BrightnessPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int target))
        {
            Drag(Math.Clamp(target, 0, 100));
            return true;
        }

        return false;
    }

    public override void Push()
    {
        var supported = _levels.Where(l => l.Supported).ToList();

        Post(new
        {
            ready = _ready,
            tint = Context.Config.DisplayTint,
            level = BrightnessShift.Average(supported.Select(l => l.Percent)),
            monitors = supported.Count,
            unsupported = _levels.Where(l => !l.Supported).Select(l => l.Name).ToArray()
        });
    }

    private async Task OpenMonitorsAsync()
    {
        // Enumerating asks every monitor for its range: seconds, on a bad day. Never on the UI thread.
        var monitors = await Task.Run(() => new MonitorBrightness());
        var levels = await Task.Run(monitors.Read);

        if (_stopped)
        {
            monitors.Dispose();
            return;
        }

        lock (_gate) _monitors = monitors;
        _levels = levels;
        _ready = true;
        Push();
    }

    private void Drag(int target)
    {
        if (!_ready) return;

        _dragStart ??= _levels.Where(l => l.Supported).Select(l => l.Percent).ToArray();
        int[] shifted = BrightnessShift.Apply(_dragStart, target);

        // Show it at once; the monitors catch up behind.
        int next = 0;
        _levels = _levels.Select(l => l.Supported ? l with { Percent = shifted[next++] } : l).ToList();

        Queue(_levels.Select(l => l.Percent).ToArray());
        Push();
    }

    private void Queue(int[] percents)
    {
        lock (_gate)
        {
            _pending = percents;
            if (_applying) return;
            _applying = true;
        }

        _ = Task.Run(ApplyLoopAsync);
    }

    private async Task ApplyLoopAsync()
    {
        while (true)
        {
            int[]? next;
            MonitorBrightness? monitors;

            lock (_gate)
            {
                next = _pending;
                _pending = null;
                monitors = _monitors;

                if (next is null || monitors is null || _stopped)
                {
                    _applying = false;
                    if (_stopped)
                    {
                        _monitors?.Dispose();
                        _monitors = null;
                    }
                    return;
                }
            }

            monitors.Set(next);
            await Task.Delay(ApplyInterval);
        }
    }

    /// <summary>After a drag, read back what the monitors actually settled on — they round to their own steps.</summary>
    private async Task RefreshLevelsAsync()
    {
        await Task.Delay(ApplyInterval * 3);

        MonitorBrightness? monitors;
        lock (_gate) monitors = _monitors;
        if (monitors is null || _stopped || _dragStart is not null) return;

        var levels = await Task.Run(monitors.Read);
        if (_stopped || _dragStart is not null) return;

        _levels = levels;
        Push();
    }

    private void ToggleTint()
    {
        Context.Config.DisplayTint = !Context.Config.DisplayTint;
        Context.Config.Save();

        if (Context.Config.DisplayTint) GammaTint.Apply();
        else GammaTint.Reset();

        Push();
    }

    private void OnTick()
    {
        if (++_ticks % TintRefreshTicks == 0 && Context.Config.DisplayTint) GammaTint.Apply();
    }
}
```

In `WidgetFactory.cs`, add before the `preset` arm:

```csharp
        { Kind: "display" } => new DisplayWidget(context),
```

In `src/Deck.Shell/App.xaml.cs`, change `Release()` to:

```csharp
    private static void Release()
    {
        var release = ReleaseScreenSpace;
        ReleaseScreenSpace = null;   // idempotent: several exit paths can fire
        release?.Invoke();

        // The reading tint lives in the display gamma ramps, which outlive the process; a crash
        // must not leave every screen orange until the next reboot.
        try
        {
            Deck.Shell.Display.GammaTint.Reset();
        }
        catch
        {
            // Nothing more can be done on the way down.
        }
    }
```

- [ ] **Step 7: Build and test**

Run the build (`0 Warning(s)`, `0 Error(s)`) and the tests (`Failed: 0`).

- [ ] **Step 8: Render the tile**

`src/Deck.Shell/ui/widgets/display.js` (no comments):

```js
Widgets.display = {
  click: 'press',
  template: () => `
    <div class="label">DISPLAY</div>
    <div class="meter-wrap disp-meter">
      <div class="meter"><div class="track"><div class="fill disp-fill"></div></div></div>
    </div>
    <div class="disp-level">…</div>
    <div class="sub disp-tint">tint off</div>
    <div class="device disp-note"></div>`,
  bind(tile, send) {
    tile.display = {
      drag: horizontalDrag(q(tile, '.disp-meter'), () => q(tile, '.meter'), (v) => {
        q(tile, '.disp-fill').style.width = v + '%';
        setText(tile, '.disp-level', v + '%');
        send('brightness:' + v);
      }, () => send('brightness-commit'))
    };
  },
  update(tile, d) {
    tile.classList.toggle('tint', d.tint);
    tile.classList.toggle('unavailable', d.ready && d.monitors === 0);
    setText(tile, '.disp-tint', d.tint ? 'warm tint on' : 'tint off');
    if (!tile.display.drag.active) {
      q(tile, '.disp-fill').style.width = (d.ready ? d.level : 0) + '%';
      setText(tile, '.disp-level', !d.ready ? '…' : d.monitors === 0 ? 'n/a' : d.level + '%');
    }
    setText(tile, '.disp-note',
      d.unsupported.length ? 'no brightness: ' + d.unsupported.join(', ')
      : d.monitors > 1 ? d.monitors + ' screens'
      : '');
    tile.title = 'Drag the bar to dim or brighten · click to toggle the warm reading tint';
  }
};
```

`src/Deck.Shell/ui/widgets/display.css`:

```css
/* --- display: the bar is brightness (drag), the rest of the tile is the tint switch (click).
   Reuses the noise meter's .meter-wrap/.meter/.track/.fill shapes --- */
.w-display .disp-level { font-size: 20px; font-weight: 650; font-variant-numeric: tabular-nums; }
.w-display .meter .fill { background: var(--text); transition: none; }
.tile.w-display.tint { background: #33270e; border-color: #6b5312; }
.w-display.tint .label, .w-display.tint .disp-tint { color: var(--warn); }
.w-display.tint .meter .fill { background: var(--warn); }
.w-display.unavailable .disp-level { color: var(--dim); }
```

Add the css link and js script to `deck.html` and `harness.html` as in Task 3. In `harness.js` `SAMPLE`, after the `network` entry (add a comma after it):

```js
    display: { ready: true, tint: false, level: 38, monitors: 3, unsupported: [] }
```

- [ ] **Step 9: Check it in the harness**

In the harness at 1077×519, place Display:
1. Screenshot: "DISPLAY", a bar filled to 38%, "38%", "tint off" and "3 screens".
2. Drag along the bar. The fill and the percentage follow the pointer, and `harness.log` receives `brightness:<n>` messages, then `brightness-commit` on release.
3. Click the tile away from the bar. The last log entry should be `widget:{"kind":"display","ref":null,"msg":"press"}`. Clicking the bar itself does not send `press`.
4. Run `harness.emit({type:'widget',kind:'display',ref:null,data:{ready:true,tint:true,level:38,monitors:3,unsupported:['LG TV']}})` and screenshot an amber tile with "warm tint on" and "no brightness: LG TV".
5. The console has no errors. Stop the server.

The real monitors and gamma are checked on the installed deck in Task 8. Don't run the app.

- [ ] **Step 10: Commit**

```bash
git add src/Deck.Shell tests/Deck.Shell.Tests
git commit -m "Add the display widget: shared brightness and a warm reading tint" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Calendar connection

**Files:**
- Modify: `src/Deck.Shell/Deck.Shell.csproj` (package), `src/Deck.Shell/Widgets/WidgetContext.cs`, `src/Deck.Shell/MainWindow.xaml.cs`
- Create: `src/Deck.Shell/Calendars/CalendarEntry.cs`, `src/Deck.Shell/Calendars/MeetingLinks.cs`, `src/Deck.Shell/Calendars/CalendarParser.cs`, `src/Deck.Shell/Calendars/CalendarService.cs`, `src/Deck.Shell/CalendarWindow.xaml`, `src/Deck.Shell/CalendarWindow.xaml.cs`, `src/Deck.Shell/ui/calendar.html`
- Test: `tests/Deck.Shell.Tests/MeetingLinksTests.cs`, `tests/Deck.Shell.Tests/CalendarParserTests.cs`

**Interfaces:**
- Consumes: `DeckConfig.CalendarLinks` (Task 1), `SharedService` (existing).
- Produces:
  - `CalendarEntry(string Title, DateTime Start, DateTime End, bool AllDay, string? JoinUrl)`
  - `MeetingLinks.Find(params string?[] fields)`
  - `CalendarParser.Load(string ics)`, which returns `Ical.Net.Calendar`
  - `CalendarParser.Entries(Ical.Net.Calendar, DateTime fromLocal, DateTime toLocal)`, which returns `List<CalendarEntry>`
  - `CalendarService(DeckConfig)` with:
    - `SharedService` Acquire/Release
    - `Task RefreshAsync()`
    - `List<CalendarEntry> Entries(DateTime fromLocal, DateTime toLocal)`
    - `string Health` (`"none"`, `"offline"` or `"ok"`)
    - `IReadOnlyList<CalendarLinkStatus> Status`
    - `event Action? Updated`
    - `static string Mask(string link)`
  - `WidgetContext.Calendar : CalendarService`
  - Tray item "Calendar…"

- [ ] **Step 1: Add the package**

In `src/Deck.Shell/Deck.Shell.csproj`, add to the `PackageReference` item group:

```xml
    <PackageReference Include="Ical.Net" Version="5.2.3" />
```

- [ ] **Step 2: Write the failing tests**

`tests/Deck.Shell.Tests/MeetingLinksTests.cs`:

```csharp
using Deck.Shell.Calendars;

namespace Deck.Shell.Tests;

public class MeetingLinksTests
{
    [Theory]
    [InlineData("Join: https://meet.google.com/abc-defg-hij now", "https://meet.google.com/abc-defg-hij")]
    [InlineData("https://us02web.zoom.us/j/123456789?pwd=abcDEF", "https://us02web.zoom.us/j/123456789?pwd=abcDEF")]
    [InlineData("https://zoom.us/j/987.", "https://zoom.us/j/987")]
    [InlineData("<https://teams.microsoft.com/l/meetup-join/19%3ameeting_x%40thread.v2/0?context=%7b%7d>", "https://teams.microsoft.com/l/meetup-join/19%3ameeting_x%40thread.v2/0?context=%7b%7d")]
    [InlineData("Webex: https://acme.webex.com/meet/jdoe", "https://acme.webex.com/meet/jdoe")]
    public void Finds_the_join_link(string text, string expected) =>
        Assert.Equal(expected, MeetingLinks.Find(text));

    [Fact]
    public void Returns_null_when_there_is_no_meeting()
    {
        Assert.Null(MeetingLinks.Find("Lunch at the usual place", null, "", "https://example.com/menu"));
    }

    [Fact]
    public void Earlier_fields_win()
    {
        Assert.Equal(
            "https://meet.google.com/aaa-bbbb-ccc",
            MeetingLinks.Find("https://meet.google.com/aaa-bbbb-ccc", "https://zoom.us/j/1"));
        Assert.Equal("https://zoom.us/j/1", MeetingLinks.Find(null, "https://zoom.us/j/1"));
    }
}
```

`tests/Deck.Shell.Tests/CalendarParserTests.cs`:

```csharp
using Deck.Shell.Calendars;

namespace Deck.Shell.Tests;

public class CalendarParserTests
{
    // A weekly Tue/Thu standup in London time with one week skipped (EXDATE 29 Sep) and one
    // moved (1 Oct 09:00 → 11:00), an all-day holiday, and a one-off UTC call.
    private const string Fixture = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:deck-tests
        BEGIN:VEVENT
        UID:weekly-1
        DTSTART;TZID=Europe/London:20260901T090000
        DTEND;TZID=Europe/London:20260901T093000
        RRULE:FREQ=WEEKLY;BYDAY=TU,TH
        EXDATE;TZID=Europe/London:20260929T090000
        SUMMARY:Standup
        DESCRIPTION:Join https://meet.google.com/abc-defg-hij
        END:VEVENT
        BEGIN:VEVENT
        UID:weekly-1
        RECURRENCE-ID;TZID=Europe/London:20261001T090000
        DTSTART;TZID=Europe/London:20261001T110000
        DTEND;TZID=Europe/London:20261001T113000
        SUMMARY:Standup (moved)
        END:VEVENT
        BEGIN:VEVENT
        UID:allday-1
        DTSTART;VALUE=DATE:20260925
        DTEND;VALUE=DATE:20260926
        SUMMARY:Holiday
        END:VEVENT
        BEGIN:VEVENT
        UID:utc-1
        DTSTART:20260924T130000Z
        DTEND:20260924T140000Z
        SUMMARY:Utc call
        LOCATION:https://zoom.us/j/123
        END:VEVENT
        END:VCALENDAR
        """;

    private static DateTime Local(int month, int day, int hourUtc, int minuteUtc = 0) =>
        new DateTime(2026, month, day, hourUtc, minuteUtc, 0, DateTimeKind.Utc).ToLocalTime();

    [Fact]
    public void Expands_repeats_skips_and_moves_into_local_time()
    {
        var calendar = CalendarParser.Load(Fixture);

        // Wide enough to be time-zone independent for the machine running the test.
        var entries = CalendarParser.Entries(calendar, new DateTime(2026, 9, 23), new DateTime(2026, 10, 5));

        Assert.Equal(
            new[] { "Standup", "Utc call", "Holiday", "Standup (moved)" },
            entries.Select(e => e.Title));

        // London 09:00 BST is 08:00 UTC.
        Assert.Equal(Local(9, 24, 8), entries[0].Start);
        Assert.Equal(Local(9, 24, 8, 30), entries[0].End);
        Assert.Equal("https://meet.google.com/abc-defg-hij", entries[0].JoinUrl);

        Assert.Equal(Local(9, 24, 13), entries[1].Start);
        Assert.Equal("https://zoom.us/j/123", entries[1].JoinUrl);

        Assert.True(entries[2].AllDay);
        Assert.Equal(new DateTime(2026, 9, 25), entries[2].Start);
        Assert.Equal(new DateTime(2026, 9, 26), entries[2].End);

        Assert.Equal(Local(10, 1, 10), entries[3].Start);
        Assert.Null(entries[3].JoinUrl);

        Assert.DoesNotContain(entries, e => e.Start.Date == new DateTime(2026, 9, 29));
    }

    [Fact]
    public void Events_outside_the_window_are_left_out()
    {
        var calendar = CalendarParser.Load(Fixture);

        var entries = CalendarParser.Entries(calendar, new DateTime(2026, 11, 2), new DateTime(2026, 11, 3));

        Assert.All(entries, e => Assert.True(e.End > new DateTime(2026, 11, 2) && e.Start < new DateTime(2026, 11, 3)));
    }

    [Fact]
    public void Text_that_is_not_a_calendar_is_refused() =>
        Assert.ThrowsAny<Exception>(() => CalendarParser.Load("<html>login required</html>"));

    [Fact]
    public void Links_are_masked_for_display()
    {
        Assert.Equal(
            "calendar.google.com/…abc.ics",
            CalendarService.Mask("https://calendar.google.com/calendar/ical/me%40x.com/private-0123456789abc/basic_abc.ics"));
        Assert.Equal("not a link", CalendarService.Mask("not a link"));
    }
}
```

- [ ] **Step 3: Run them to confirm they fail**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS because `Deck.Shell.Calendars` doesn't exist.

- [ ] **Step 4: Write the calendar code**

`src/Deck.Shell/Calendars/CalendarEntry.cs`:

```csharp
namespace Deck.Shell.Calendars;

/// <summary>One occurrence of an event, in local time. All-day entries run from local midnight of their first day.</summary>
internal sealed record CalendarEntry(string Title, DateTime Start, DateTime End, bool AllDay, string? JoinUrl);
```

`src/Deck.Shell/Calendars/MeetingLinks.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Deck.Shell.Calendars;

/// <summary>
/// Finds the "join" link in an event. Invitations bury it in different places — Google puts Meet
/// in its own conference field, Zoom and Teams paste theirs into the location or the
/// description — so every field is searched, the most reliable first.
/// </summary>
internal static partial class MeetingLinks
{
    [GeneratedRegex(
        @"https://(?:meet\.google\.com/[a-z]{3}-[a-z]{4}-[a-z]{3}|(?:[\w-]+\.)?zoom\.us/(?:j|my|w)/[^\s""'<>)]+|teams\.microsoft\.com/l/meetup-join/[^\s""'<>)]+|teams\.live\.com/meet/[^\s""'<>)]+|[\w-]+\.webex\.com/[^\s""'<>)]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static string? Find(params string?[] fields)
    {
        foreach (string? field in fields)
        {
            if (string.IsNullOrEmpty(field)) continue;

            var match = Pattern().Match(field);

            // A sentence that ends on the link leaves its full stop attached.
            if (match.Success) return match.Value.TrimEnd('.', ',', ';');
        }

        return null;
    }
}
```

`src/Deck.Shell/Calendars/CalendarParser.cs`:

```csharp
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using IcalCalendar = Ical.Net.Calendar;

namespace Deck.Shell.Calendars;

/// <summary>
/// Turns iCal text into local-time entries for a window of time. Ical.Net does the hard parts:
/// repeats, skipped and moved instances, time zones.
/// </summary>
internal static class CalendarParser
{
    /// <summary>A stop for a malformed rule that would otherwise repeat forever.</summary>
    private const int MaxOccurrences = 5000;

    /// <summary>Throws on text that isn't iCal (a login page, an error page); the service turns that into the link's status.</summary>
    public static IcalCalendar Load(string ics) =>
        IcalCalendar.Load(ics) ?? throw new FormatException("not an iCal calendar");

    public static List<CalendarEntry> Entries(IcalCalendar calendar, DateTime fromLocal, DateTime toLocal)
    {
        // A day early, so a meeting already in progress at fromLocal is still found.
        var start = new CalDateTime(fromLocal.AddDays(-1).ToUniversalTime(), CalDateTime.UtcTzId);

        // Occurrences come in start order. All-day ones are "floating" dates that sort by their
        // UTC reading, so allow a day's slack before stopping.
        DateTime stopUtc = toLocal.ToUniversalTime().AddDays(1);

        var entries = new List<CalendarEntry>();

        foreach (var occurrence in calendar.GetOccurrences<CalendarEvent>(start, null).Take(MaxOccurrences))
        {
            var period = occurrence.Period;
            if (period.StartTime.AsUtc > stopUtc) break;
            if (occurrence.Source is not CalendarEvent ev) continue;

            bool allDay = ev.IsAllDay || !period.StartTime.HasTime;
            var endTime = period.EffectiveEndTime ?? period.StartTime;

            DateTime begin = allDay ? period.StartTime.Date.ToDateTime(TimeOnly.MinValue) : period.StartTime.AsUtc.ToLocalTime();
            DateTime end = allDay ? endTime.Date.ToDateTime(TimeOnly.MinValue) : endTime.AsUtc.ToLocalTime();
            if (allDay && end <= begin) end = begin.AddDays(1);

            if (begin >= toLocal || end <= fromLocal) continue;

            string? conference = ev.Properties.FirstOrDefault(p => p.Name == "X-GOOGLE-CONFERENCE")?.Value?.ToString();

            entries.Add(new CalendarEntry(
                string.IsNullOrWhiteSpace(ev.Summary) ? "(no title)" : ev.Summary.Trim(),
                begin,
                end,
                allDay,
                MeetingLinks.Find(conference, ev.Location, ev.Description, ev.Url?.ToString())));
        }

        return entries.OrderBy(e => e.Start).ToList();
    }
}
```

`src/Deck.Shell/Calendars/CalendarService.cs`:

```csharp
using System.Net.Http;
using System.Runtime.Serialization;
using System.Windows.Threading;
using Deck.Shell.Config;
using Deck.Shell.Widgets;
using IcalCalendar = Ical.Net.Calendar;

namespace Deck.Shell.Calendars;

internal sealed record CalendarLinkStatus(string Link, bool Ok, string Message);

/// <summary>
/// The calendar behind Agenda and Month. While either is on the deck it fetches every link every
/// 10 minutes. A link that fails keeps its last good calendar, so a flaky connection shows
/// slightly old events rather than none.
/// </summary>
internal sealed class CalendarService : SharedService, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly DeckConfig _config;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private readonly Dictionary<string, IcalCalendar> _calendars = new(StringComparer.Ordinal);
    private bool _refreshing;
    private bool _refreshAgain;
    private bool _fetchedOnce;

    public CalendarService(DeckConfig config)
    {
        _config = config;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public IReadOnlyList<CalendarLinkStatus> Status { get; private set; } = [];

    /// <summary>Raised on the UI thread after every refresh, successful or not.</summary>
    public event Action? Updated;

    /// <summary>"none" with no links, "offline" when nothing could be loaded at all, otherwise "ok".</summary>
    public string Health =>
        _config.CalendarLinks.Count == 0 ? "none"
        : _fetchedOnce && _calendars.Count == 0 ? "offline"
        : "ok";

    protected override void OnStart()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    protected override void OnStop() => _timer.Stop();

    /// <summary>Safe to call at any time. A call during a refresh runs another one straight after, so a Save is never lost.</summary>
    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                await RefreshOnceAsync();
            }
            while (_refreshAgain);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public List<CalendarEntry> Entries(DateTime fromLocal, DateTime toLocal)
    {
        var entries = new List<CalendarEntry>();

        foreach (var calendar in _calendars.Values)
        {
            try
            {
                entries.AddRange(CalendarParser.Entries(calendar, fromLocal, toLocal));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
            {
                // A rule Ical.Net can't evaluate spoils only its own calendar's answer.
            }
        }

        return entries.OrderBy(e => e.Start).ToList();
    }

    /// <summary>"calendar.google.com/…abc.ics": enough to tell links apart, not enough to use one.</summary>
    public static string Mask(string link)
    {
        if (!Uri.TryCreate(ToHttps(link), UriKind.Absolute, out var uri)) return link;

        string tail = uri.AbsolutePath.Length > 7 ? uri.AbsolutePath[^7..] : uri.AbsolutePath;
        return $"{uri.Host}/…{tail}";
    }

    public void Dispose()
    {
        _timer.Stop();
        _http.Dispose();
    }

    private async Task RefreshOnceAsync()
    {
        var links = _config.CalendarLinks.ToList();
        var status = new List<CalendarLinkStatus>();

        foreach (string link in links)
        {
            try
            {
                string ics = await _http.GetStringAsync(ToHttps(link));
                var calendar = await Task.Run(() => CalendarParser.Load(ics));
                _calendars[link] = calendar;
                status.Add(new CalendarLinkStatus(link, true, $"OK · {calendar.Events.Count} events"));
            }
            catch (Exception ex)
            {
                // Every failure is the same to the user — this link didn't load — so all of them
                // become its status instead of an exception.
                status.Add(new CalendarLinkStatus(link, false, Describe(ex)));
            }
        }

        foreach (string gone in _calendars.Keys.Except(links).ToList()) _calendars.Remove(gone);

        Status = status;
        _fetchedOnce = true;
        Updated?.Invoke();
    }

    /// <summary>Calendar apps hand out webcal:// links; they're plain https underneath.</summary>
    private static string ToHttps(string link) =>
        link.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + link["webcal://".Length..] : link;

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => $"the server said {(int)code}",
        HttpRequestException => "couldn't reach the server",
        TaskCanceledException => "timed out",
        SerializationException or FormatException => "that isn't an iCal calendar",
        UriFormatException or InvalidOperationException => "that isn't a valid link",
        _ => ex.Message
    };
}
```

- [ ] **Step 5: Run the tests to confirm they pass**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Failed: 0`.

- [ ] **Step 6: Hand the service to widgets**

In `src/Deck.Shell/Widgets/WidgetContext.cs`, add `using Deck.Shell.Calendars;` and, after the `Privacy` property:

```csharp
    /// <summary>The iCal feeds behind Agenda and Month. Also owned by the main window, which the Calendar window needs.</summary>
    public required CalendarService Calendar { get; init; }
```

- [ ] **Step 7: Write the Calendar window**

`src/Deck.Shell/CalendarWindow.xaml`:

```xml
<Window x:Class="Deck.Shell.CalendarWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
        Title="Calendar"
        Width="640" Height="520"
        MinWidth="480" MinHeight="380"
        WindowStartupLocation="CenterScreen"
        Background="#0B0D10">

    <Grid>
        <wv2:WebView2 x:Name="Web" DefaultBackgroundColor="#0B0D10" />
    </Grid>
</Window>
```

`src/Deck.Shell/CalendarWindow.xaml.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Windows;
using Deck.Shell.Calendars;
using Deck.Shell.Config;
using Microsoft.Web.WebView2.Core;

namespace Deck.Shell;

/// <summary>
/// Where iCal links are pasted. An ordinary window, because pasting needs the keyboard. The links
/// themselves only appear in the text box; the status list below shows them masked.
/// </summary>
public partial class CalendarWindow : Window
{
    private readonly DeckConfig _config;
    private readonly CalendarService _calendar;
    private bool _checking;

    internal CalendarWindow(DeckConfig config, CalendarService calendar)
    {
        InitializeComponent();
        _config = config;
        _calendar = calendar;
        Loaded += OnLoaded;
        Closed += (_, _) => _calendar.Updated -= OnUpdated;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Deck", "WebView2");

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(env);

            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.WebMessageReceived += OnWebMessage;
            Web.CoreWebView2.NavigationCompleted += (_, _) => SendState();
            _calendar.Updated += OnUpdated;

            string path = Path.Combine(AppContext.BaseDirectory, "ui", "calendar.html");
            Web.CoreWebView2.NavigateToString(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Calendar window failed to start");
            Close();
        }
    }

    private void OnUpdated()
    {
        _checking = false;
        SendState();
    }

    private void SendState()
    {
        if (Web.CoreWebView2 is null) return;

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "calendar",
            links = _config.CalendarLinks,
            checking = _checking,
            status = _calendar.Status
                .Where(s => _config.CalendarLinks.Contains(s.Link))
                .Select(s => new { label = CalendarService.Mask(s.Link), ok = s.Ok, message = s.Message })
        }));
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        if (message == "close")
        {
            Close();
            return;
        }

        const string savePrefix = "save:";
        if (!message.StartsWith(savePrefix, StringComparison.Ordinal)) return;

        string[]? lines;
        try
        {
            lines = JsonSerializer.Deserialize<string[]>(message[savePrefix.Length..]);
        }
        catch (JsonException)
        {
            return;
        }

        _config.CalendarLinks = (lines ?? [])
            .Select(l => l.Trim())
            .Where(IsLink)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _config.Save();

        _checking = true;
        SendState();
        _ = _calendar.RefreshAsync();
    }

    private static bool IsLink(string line) =>
        Uri.TryCreate(line, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" or "webcal";
}
```

`src/Deck.Shell/ui/calendar.html` (the inline script must have no comments):

```html
<meta charset="utf-8" />
<title>Calendar</title>
<style>
  :root {
    --bg: #0b0d10;
    --panel: #171b21;
    --line: #2a313b;
    --text: #e6eaf0;
    --dim: #8b95a4;
    --accent: #3fb950;
    --danger: #f85149;
  }

  * { box-sizing: border-box; }

  html, body {
    margin: 0;
    height: 100%;
    background: var(--bg);
    color: var(--text);
    font: 14px/1.45 "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
  }

  body { display: grid; grid-template-rows: auto 1fr auto; }

  header { padding: 20px 24px 10px; }
  h1 { margin: 0 0 4px; font-size: 19px; font-weight: 650; }
  .hint { color: var(--dim); font-size: 13px; }

  main { padding: 6px 24px; display: flex; flex-direction: column; gap: 12px; min-height: 0; }

  textarea {
    flex: 1;
    min-height: 110px;
    resize: none;
    background: #0e1116;
    border: 1px solid var(--line);
    border-radius: 8px;
    color: var(--text);
    padding: 10px 12px;
    font: 12.5px/1.5 Consolas, "Cascadia Mono", monospace;
  }
  textarea:focus { outline: none; border-color: var(--accent); }

  .status { display: flex; flex-direction: column; gap: 6px; font-size: 13px; }
  .status .row { display: flex; justify-content: space-between; gap: 12px; padding: 8px 12px; border: 1px solid var(--line); border-radius: 8px; background: var(--panel); }
  .status .ok { color: var(--accent); }
  .status .bad { color: var(--danger); }
  .status .checking { color: var(--dim); }

  footer {
    padding: 14px 24px 20px;
    border-top: 1px solid var(--line);
    display: flex;
    gap: 12px;
    justify-content: flex-end;
  }

  button {
    border-radius: 8px;
    padding: 9px 18px;
    font: inherit;
    font-weight: 600;
    cursor: pointer;
    border: 1px solid var(--line);
    background: var(--panel);
    color: var(--text);
  }
  button.primary { background: var(--accent); border-color: var(--accent); color: #08130b; }
  button:hover { filter: brightness(1.12); }
</style>

<header>
  <h1>Calendar</h1>
  <div class="hint">
    Paste your calendar's secret iCal address, one per line. In Google Calendar: Settings →
    your calendar → Integrate calendar → Secret address in iCal format. The deck keeps these on
    this PC only.
  </div>
</header>

<main>
  <textarea id="links" spellcheck="false" placeholder="https://calendar.google.com/calendar/ical/…/basic.ics"></textarea>
  <div class="status" id="status"></div>
</main>

<footer>
  <button id="close">Close</button>
  <button id="save" class="primary">Save</button>
</footer>

<script>
  const post = (m) => window.chrome.webview.postMessage(m);
  const $ = (id) => document.getElementById(id);
  let loaded = false;

  window.chrome.webview.addEventListener('message', (ev) => {
    const d = ev.data;
    if (d.type !== 'calendar') return;
    if (!loaded) {
      $('links').value = d.links.join('\n');
      loaded = true;
    }
    const host = $('status');
    host.innerHTML = '';
    if (d.checking) {
      const row = document.createElement('div');
      row.className = 'row checking';
      row.textContent = 'Checking…';
      host.append(row);
      return;
    }
    for (const s of d.status) {
      const row = document.createElement('div');
      row.className = 'row';
      const label = document.createElement('span');
      label.textContent = s.label;
      const result = document.createElement('span');
      result.className = s.ok ? 'ok' : 'bad';
      result.textContent = s.message;
      row.append(label, result);
      host.append(row);
    }
  });

  $('close').addEventListener('click', () => post('close'));
  $('save').addEventListener('click', () => {
    const lines = $('links').value.split('\n').map((l) => l.trim()).filter((l) => l.length > 0);
    post('save:' + JSON.stringify(lines));
  });
</script>
```

- [ ] **Step 8: Wire the host**

In `src/Deck.Shell/MainWindow.xaml.cs`:

1. Add `using Deck.Shell.Calendars;`, and add these fields next to `_media`:

```csharp
    private CalendarService? _calendar;
    private CalendarWindow? _calendarWindow;
```

2. In `OnLoaded`, add the tray item after `Shortcuts…`:

```csharp
        _notifier.AddItem("Calendar…", OpenCalendar);
```

3. In `StartWidgets`, create the service after `_media = new MediaService();`:

```csharp
        _calendar = new CalendarService(_config);
```

   and add `Calendar = _calendar,` to the `WidgetContext` initializer after `Privacy = …,`.

4. Add this method after `OpenDevices`:

```csharp
    private void OpenCalendar()
    {
        if (_calendar is null) return;

        if (_calendarWindow is { IsVisible: true })
        {
            _calendarWindow.Activate();
            return;
        }

        _calendarWindow = new CalendarWindow(_config, _calendar);
        _calendarWindow.Closed += (_, _) => _calendarWindow = null;
        _calendarWindow.Show();
        _calendarWindow.Activate();
    }
```

5. In `Cleanup`, add `_calendar?.Dispose();` directly after `_media?.Dispose();`.

- [ ] **Step 9: Build and test**

Run: `dotnet build src/Deck.Shell/Deck.Shell.csproj -nologo -v q` → `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Failed: 0`.
Check that `src/Deck.Shell/bin/Debug/net10.0-windows10.0.19041.0/Ical.Net.dll` exists.

- [ ] **Step 10: Commit**

```bash
git add src/Deck.Shell tests/Deck.Shell.Tests
git commit -m "Add the shared iCal calendar connection and its settings window" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Agenda and Month

**Files:**
- Create: `src/Deck.Shell/Calendars/AgendaText.cs`, `src/Deck.Shell/Calendars/MonthGrid.cs`, `src/Deck.Shell/Widgets/AgendaWidget.cs`, `src/Deck.Shell/Widgets/MonthWidget.cs`, `src/Deck.Shell/ui/widgets/agenda.js`, `src/Deck.Shell/ui/widgets/agenda.css`, `src/Deck.Shell/ui/widgets/month.js`, `src/Deck.Shell/ui/widgets/month.css`
- Modify: `src/Deck.Shell/Widgets/WidgetFactory.cs`, `src/Deck.Shell/ui/deck.html`, `src/Deck.Shell/ui/dev/harness.html`, `src/Deck.Shell/ui/dev/harness.js`
- Test: `tests/Deck.Shell.Tests/AgendaTextTests.cs`, `tests/Deck.Shell.Tests/MonthGridTests.cs`

**Interfaces:**
- Consumes: `CalendarEntry`, and from `CalendarService` via `Context.Calendar`: `Entries`, `Health`, `Updated` and Acquire/Release (Task 6). Also `Context.Tick`.
- Produces:
  - `AgendaState { Later, Soon, Now }`
  - `AgendaText.State(...)`, `AgendaText.When(...)` and `AgendaText.Upcoming(entries, now)`
  - `MonthGrid.FirstCell(year, month)` and `MonthGrid.Build(year, month, today, entries)`, which returns `MonthView(Title, Days)` with `MonthDay(Date, InMonth, IsToday, Events)`
  - Agenda posts `{status, events:[{title, when, state, link}], total}` and handles `press` and `next`
  - Month posts `{title, status, days:[{day, inMonth, today, events}]}` and handles `prev`, `next` and `today`

- [ ] **Step 1: Write the failing tests**

`tests/Deck.Shell.Tests/AgendaTextTests.cs`:

```csharp
using Deck.Shell.Calendars;

namespace Deck.Shell.Tests;

public class AgendaTextTests
{
    // Thursday 24 Sep 2026, 14:35.
    private static readonly DateTime Now = new(2026, 9, 24, 14, 35, 0);

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0);

    [Theory]
    [InlineData(24, 14, 0, 15, 0, "now · until 15:00", "Now")]
    [InlineData(24, 14, 38, 15, 0, "in 3 min", "Soon")]
    [InlineData(24, 14, 47, 15, 0, "in 12 min", "Later")]
    [InlineData(24, 15, 35, 16, 0, "in 60 min", "Later")]
    [InlineData(24, 18, 0, 19, 0, "18:00", "Later")]
    [InlineData(25, 9, 0, 9, 30, "Tomorrow 09:00", "Later")]
    [InlineData(29, 9, 0, 9, 30, "Tue 09:00", "Later")]
    public void Says_when_in_the_way_a_glance_needs(int day, int h, int m, int endH, int endM, string when, string state)
    {
        var start = At(day, h, m);
        var end = At(day, endH, endM);

        Assert.Equal(when, AgendaText.When(start, end, Now));
        Assert.Equal(state, AgendaText.State(start, end, Now).ToString());
    }

    [Fact]
    public void Upcoming_skips_ended_and_all_day_events_and_sorts()
    {
        var entries = new[]
        {
            new CalendarEntry("Later", At(24, 18), At(24, 19), false, null),
            new CalendarEntry("Ended", At(24, 9), At(24, 10), false, null),
            new CalendarEntry("Holiday", At(24, 0), At(25, 0), true, null),
            new CalendarEntry("Running", At(24, 14), At(24, 15), false, "https://zoom.us/j/1")
        };

        Assert.Equal(new[] { "Running", "Later" }, AgendaText.Upcoming(entries, Now).Select(e => e.Title));
    }
}
```

`tests/Deck.Shell.Tests/MonthGridTests.cs`:

```csharp
using Deck.Shell.Calendars;

namespace Deck.Shell.Tests;

public class MonthGridTests
{
    [Fact]
    public void Always_six_monday_first_weeks()
    {
        // 1 Sep 2026 is a Tuesday, so the grid opens on Monday 31 August.
        var view = MonthGrid.Build(2026, 9, today: new DateTime(2026, 9, 24), entries: []);

        Assert.Equal("September 2026", view.Title);
        Assert.Equal(42, view.Days.Count);
        Assert.Equal(new DateTime(2026, 8, 31), view.Days[0].Date);
        Assert.Equal(new DateTime(2026, 10, 11), view.Days[41].Date);
        Assert.Equal(30, view.Days.Count(d => d.InMonth));
        Assert.False(view.Days[0].InMonth);
        Assert.Equal(new DateTime(2026, 8, 31), MonthGrid.FirstCell(2026, 9));
    }

    [Fact]
    public void Marks_today_and_lists_each_days_events()
    {
        var entries = new[]
        {
            new CalendarEntry("Standup", new DateTime(2026, 9, 24, 11, 0, 0), new DateTime(2026, 9, 24, 11, 30, 0), false, null),
            new CalendarEntry("Trip", new DateTime(2026, 9, 25), new DateTime(2026, 9, 28), true, null)
        };

        var view = MonthGrid.Build(2026, 9, today: new DateTime(2026, 9, 24), entries);
        MonthDay Day(int d) => view.Days.Single(x => x.Date == new DateTime(2026, 9, d));

        Assert.True(Day(24).IsToday);
        Assert.Equal(new[] { "11:00 Standup" }, Day(24).Events);
        Assert.Equal(new[] { "Trip" }, Day(25).Events);
        Assert.Equal(new[] { "Trip" }, Day(27).Events);
        Assert.Empty(Day(28).Events);
        Assert.Single(view.Days, d => d.IsToday);
    }

    [Fact]
    public void A_month_starting_on_monday_starts_the_grid_that_day() =>
        Assert.Equal(new DateTime(2026, 6, 1), MonthGrid.FirstCell(2026, 6));
}
```

- [ ] **Step 2: Run them to confirm they fail**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo`
Expected: build FAILS because `AgendaText` and `MonthGrid` don't exist.

- [ ] **Step 3: Write the pure logic**

`src/Deck.Shell/Calendars/AgendaText.cs`:

```csharp
using System.Globalization;

namespace Deck.Shell.Calendars;

internal enum AgendaState { Later, Soon, Now }

/// <summary>What the agenda says about an event, relative to "now". Pure, so every boundary is tested.</summary>
internal static class AgendaText
{
    /// <summary>Close enough to start that the tile should catch your eye.</summary>
    public static readonly TimeSpan SoonWindow = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan MinutesWindow = TimeSpan.FromMinutes(60);

    public static AgendaState State(DateTime start, DateTime end, DateTime now) =>
        now >= start && now < end ? AgendaState.Now
        : start > now && start - now <= SoonWindow ? AgendaState.Soon
        : AgendaState.Later;

    public static string When(DateTime start, DateTime end, DateTime now)
    {
        if (now >= start && now < end) return "now · until " + end.ToString("HH:mm", CultureInfo.InvariantCulture);

        var until = start - now;
        if (until <= MinutesWindow) return $"in {Math.Max(1, (int)Math.Ceiling(until.TotalMinutes))} min";

        string time = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        return (start.Date - now.Date).Days switch
        {
            0 => time,
            1 => "Tomorrow " + time,
            _ => start.ToString("ddd", CultureInfo.InvariantCulture) + " " + time
        };
    }

    /// <summary>Timed events that haven't ended, soonest first. All-day events belong on the month view.</summary>
    public static List<CalendarEntry> Upcoming(IEnumerable<CalendarEntry> entries, DateTime now) =>
        entries.Where(e => !e.AllDay && e.End > now).OrderBy(e => e.Start).ToList();
}
```

`src/Deck.Shell/Calendars/MonthGrid.cs`:

```csharp
using System.Globalization;

namespace Deck.Shell.Calendars;

internal sealed record MonthDay(DateTime Date, bool InMonth, bool IsToday, IReadOnlyList<string> Events);

internal sealed record MonthView(string Title, IReadOnlyList<MonthDay> Days);

/// <summary>
/// A six-week, Monday-first grid. Always 42 days, so the tile never changes shape between months.
/// </summary>
internal static class MonthGrid
{
    public const int Cells = 42;

    public static DateTime FirstCell(int year, int month)
    {
        var first = new DateTime(year, month, 1);
        int daysSinceMonday = ((int)first.DayOfWeek + 6) % 7;
        return first.AddDays(-daysSinceMonday);
    }

    public static MonthView Build(int year, int month, DateTime today, IReadOnlyList<CalendarEntry> entries)
    {
        var start = FirstCell(year, month);
        var days = new List<MonthDay>(Cells);

        for (int i = 0; i < Cells; i++)
        {
            var date = start.AddDays(i);
            var next = date.AddDays(1);

            var events = entries
                .Where(e => e.Start < next && (e.End > date || e.Start >= date))
                .OrderBy(e => e.Start)
                .Select(e => e.AllDay ? e.Title : e.Start.ToString("HH:mm", CultureInfo.InvariantCulture) + " " + e.Title)
                .ToList();

            days.Add(new MonthDay(date, date.Month == month, date == today.Date, events));
        }

        string title = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        return new MonthView(title, days);
    }
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj -nologo` → `Failed: 0`.

- [ ] **Step 5: Write the widgets**

`src/Deck.Shell/Widgets/AgendaWidget.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Deck.Shell.Calendars;

namespace Deck.Shell.Widgets;

/// <summary>
/// The next meetings, and a one-click join. Focus starts on the soonest event; right-click steps
/// through the rest. The focused event is always the first one sent, so the 1×1 tile shows it
/// and the 2×1 tile lists it first.
/// </summary>
internal sealed class AgendaWidget(WidgetContext context) : WidgetBase(context, "agenda")
{
    private static readonly TimeSpan LookAhead = TimeSpan.FromDays(7);
    private const int Rows = 3;

    /// <summary>Re-read the calendar once a minute: often enough to drop an ended meeting, cheap enough not to matter.</summary>
    private const int ReloadTicks = 60;

    private List<CalendarEntry> _upcoming = [];
    private int _focus;
    private int _ticks;
    private string _lastShown = "";

    public override void Start()
    {
        Context.Calendar.Updated += OnUpdated;
        Context.Tick.Ticked += OnTick;
        Context.Calendar.Acquire();
        Context.Tick.Acquire();
        Reload();
    }

    public override void Stop()
    {
        Context.Calendar.Updated -= OnUpdated;
        Context.Tick.Ticked -= OnTick;
        Context.Calendar.Release();
        Context.Tick.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "press":
                Open();
                return true;

            case "next":
                int count = Visible(DateTime.Now).Count;
                if (count > 0) _focus = (_focus + 1) % count;
                Send(force: true);
                return true;

            default:
                return false;
        }
    }

    public override void Push() => Send(force: true);

    private void OnUpdated()
    {
        Reload();
        Send(force: true);
    }

    private void OnTick()
    {
        if (++_ticks % ReloadTicks == 0) Reload();
        Send(force: false);
    }

    private void Reload()
    {
        var now = DateTime.Now;
        var next = AgendaText.Upcoming(Context.Calendar.Entries(now, now + LookAhead), now);

        // A different list means the old focus points at the wrong meeting; start again at the soonest.
        if (!next.Select(Key).SequenceEqual(_upcoming.Select(Key))) _focus = 0;
        _upcoming = next;
    }

    private static string Key(CalendarEntry e) => $"{e.Title}|{e.Start:O}";

    private List<CalendarEntry> Visible(DateTime now) => _upcoming.Where(e => e.End > now).ToList();

    private void Send(bool force)
    {
        var now = DateTime.Now;
        var visible = Visible(now);
        if (_focus >= visible.Count) _focus = 0;

        var rows = visible.Skip(_focus).Concat(visible.Take(_focus)).Take(Rows).Select(e => new
        {
            title = e.Title,
            when = AgendaText.When(e.Start, e.End, now),
            state = AgendaText.State(e.Start, e.End, now).ToString().ToLowerInvariant(),
            link = e.JoinUrl is not null
        }).ToArray();

        var data = new { status = Context.Calendar.Health, events = rows, total = visible.Count };

        // Ticks every second; "in 12 min" only changes once a minute.
        string shown = JsonSerializer.Serialize(data);
        if (!force && shown == _lastShown) return;
        _lastShown = shown;

        Post(data);
    }

    /// <summary>
    /// The meeting link if there is one, otherwise Google Calendar on that day. Opening a browser
    /// takes focus, and that's the point: you're joining the meeting.
    /// </summary>
    private void Open()
    {
        var visible = Visible(DateTime.Now);

        string url;
        if (visible.Count == 0)
        {
            url = "https://calendar.google.com/calendar/r";
        }
        else
        {
            var e = visible[Math.Min(_focus, visible.Count - 1)];
            url = e.JoinUrl ?? $"https://calendar.google.com/calendar/r/day/{e.Start.Year}/{e.Start.Month}/{e.Start.Day}";
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Context.Notifier.Show("Couldn't open the meeting", ex.Message);
        }
    }
}
```

`src/Deck.Shell/Widgets/MonthWidget.cs`:

```csharp
using Deck.Shell.Calendars;

namespace Deck.Shell.Widgets;

/// <summary>A month at a glance, with a dot on any day that has something on it.</summary>
internal sealed class MonthWidget(WidgetContext context) : WidgetBase(context, "month")
{
    /// <summary>Months away from today's month; 0 is this month.</summary>
    private int _offset;

    private DateTime _today = DateTime.Today;

    public override void Start()
    {
        Context.Calendar.Updated += Push;
        Context.Tick.Ticked += OnTick;
        Context.Calendar.Acquire();
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        Context.Calendar.Updated -= Push;
        Context.Tick.Ticked -= OnTick;
        Context.Calendar.Release();
        Context.Tick.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "prev":
                _offset--;
                break;

            case "next":
                _offset++;
                break;

            case "today":
                _offset = 0;
                break;

            default:
                return false;
        }

        Push();
        return true;
    }

    public override void Push()
    {
        var shown = new DateTime(_today.Year, _today.Month, 1).AddMonths(_offset);
        var first = MonthGrid.FirstCell(shown.Year, shown.Month);
        var entries = Context.Calendar.Entries(first, first.AddDays(MonthGrid.Cells));
        var view = MonthGrid.Build(shown.Year, shown.Month, _today, entries);

        Post(new
        {
            title = view.Title,
            status = Context.Calendar.Health,
            days = view.Days.Select(d => new
            {
                day = d.Date.Day,
                inMonth = d.InMonth,
                today = d.IsToday,
                events = d.Events
            })
        });
    }

    /// <summary>Only midnight changes anything; checked every tick because a desktop can sleep straight through it.</summary>
    private void OnTick()
    {
        if (DateTime.Today == _today) return;

        _today = DateTime.Today;
        Push();
    }
}
```

In `WidgetFactory.cs`, add before the `preset` arm:

```csharp
        { Kind: "agenda" } => new AgendaWidget(context),
        { Kind: "month" } => new MonthWidget(context),
```

- [ ] **Step 6: Build and test**

Run the build (`0 Warning(s)`, `0 Error(s)`) and the tests (`Failed: 0`).

- [ ] **Step 7: Render the tiles**

`src/Deck.Shell/ui/widgets/agenda.js` (no comments):

```js
function agendaNote(d, first) {
  if (d.status === 'none') return 'connect a calendar: tray → Calendar…';
  if (d.status === 'offline') return 'calendar offline';
  if (!first) return 'nothing in the next 7 days';
  return first.link ? 'click to join' : 'click to open';
}

Widgets.agenda = {
  click: 'press',
  context: 'next',
  template: (variant) => variant === 'wide'
    ? `<div class="label">AGENDA</div><div class="ag-rows"></div><div class="device ag-note"></div>`
    : `<div class="label">AGENDA</div>
       <div class="ag-main">–</div>
       <div class="sub ag-main-when"></div>
       <div class="device ag-note"></div>`,
  update(tile, d, variant) {
    const first = d.events[0];
    for (const s of ['now', 'soon', 'later']) tile.classList.toggle(s, !!first && first.state === s);
    setText(tile, '.ag-note', agendaNote(d, first));

    if (variant === 'wide') {
      const host = q(tile, '.ag-rows');
      host.innerHTML = '';
      d.events.forEach((e, i) => {
        const row = document.createElement('div');
        row.className = 'ag-row ' + e.state + (i === 0 ? ' focus' : '');
        row.innerHTML = '<span class="ag-title"></span><span class="ag-when"></span>';
        row.children[0].textContent = (e.link ? '● ' : '') + e.title;
        row.children[1].textContent = e.when;
        host.append(row);
      });
    } else {
      setText(tile, '.ag-main', first ? first.title : '');
      setText(tile, '.ag-main-when', first ? first.when : '');
    }

    tile.title = d.total > 1
      ? 'Click to join · right-click for the next event (' + d.total + ' coming up)'
      : 'Click to join';
  }
};
```

`src/Deck.Shell/ui/widgets/agenda.css`:

```css
/* --- agenda: amber just before a meeting, green while you should be in it --- */
.w-agenda .ag-main {
  max-width: 100%;
  font-size: 14px;
  font-weight: 650;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.w-agenda .ag-main-when { font-variant-numeric: tabular-nums; }

.tile.w-agenda.soon { background: #33270e; border-color: #6b5312; }
.w-agenda.soon .label, .w-agenda.soon .ag-main, .w-agenda.soon .ag-main-when { color: var(--warn); }
.tile.w-agenda.now { background: var(--ok-bg); border-color: var(--ok-line); }
.w-agenda.now .label, .w-agenda.now .ag-main, .w-agenda.now .ag-main-when { color: var(--ok); }

.tile.w-agenda.v-wide { justify-content: flex-start; padding: 12px 14px; }
.w-agenda .ag-rows { width: 100%; display: flex; flex-direction: column; gap: 5px; margin-top: 2px; }
.w-agenda .ag-row {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 10px;
  align-items: baseline;
  padding: 5px 8px;
  border-radius: 7px;
  font-size: 12px;
  color: var(--dim);
}
.w-agenda .ag-row.focus { background: #0e1116; color: var(--text); }
.w-agenda .ag-title { text-align: left; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 600; }
.w-agenda .ag-when { font-variant-numeric: tabular-nums; white-space: nowrap; }
.w-agenda .ag-row.soon .ag-when { color: var(--warn); }
.w-agenda .ag-row.now .ag-when { color: var(--ok); }
```

`src/Deck.Shell/ui/widgets/month.js` (no comments):

```js
Widgets.month = {
  template: () => `
    <div class="mo-head">
      <button class="mo-btn" data-msg="prev" title="Previous month">‹</button>
      <button class="mo-title" data-msg="today" title="Back to this month"></button>
      <button class="mo-btn" data-msg="next" title="Next month">›</button>
    </div>
    <div class="mo-grid">${['M', 'T', 'W', 'T', 'F', 'S', 'S'].map((d) => `<span class="mo-dow">${d}</span>`).join('')}</div>
    <div class="device mo-hint"></div>`,
  bind(tile, send) {
    for (const button of tile.querySelectorAll('[data-msg]')) {
      button.addEventListener('click', (e) => {
        e.stopPropagation();
        send(button.dataset.msg);
      });
    }
  },
  update(tile, d) {
    setText(tile, '.mo-title', d.title);
    const grid = q(tile, '.mo-grid');
    for (const cell of grid.querySelectorAll('.mo-day')) cell.remove();
    for (const day of d.days) {
      const cell = document.createElement('span');
      cell.className = 'mo-day' + (day.inMonth ? '' : ' out') + (day.today ? ' today' : '') + (day.events.length ? ' has' : '');
      cell.textContent = day.day;
      if (day.events.length) cell.title = day.events.join('\n');
      grid.append(cell);
    }
    setText(tile, '.mo-hint',
      d.status === 'none' ? 'connect a calendar: tray → Calendar…'
      : d.status === 'offline' ? 'calendar offline'
      : '');
  }
};
```

`src/Deck.Shell/ui/widgets/month.css`:

```css
/* --- month: a fixed six-week grid, so the tile never changes shape between months --- */
.tile.w-month { justify-content: flex-start; gap: 6px; padding: 10px 12px; }
.w-month .mo-head { width: 100%; display: flex; align-items: center; justify-content: space-between; }
.w-month .mo-btn, .w-month .mo-title {
  background: none;
  border: none;
  color: var(--text);
  font: inherit;
  cursor: pointer;
  border-radius: 6px;
  padding: 2px 10px;
}
.w-month .mo-btn { font-size: 18px; line-height: 1; color: var(--dim); }
.w-month .mo-title { font-size: 13px; font-weight: 650; letter-spacing: 0.03em; }
.w-month .mo-btn:hover, .w-month .mo-title:hover { background: var(--tile-hi); color: var(--text); }

.w-month .mo-grid {
  width: 100%;
  flex: 1;
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  grid-template-rows: auto repeat(6, 1fr);
  gap: 2px;
}
.w-month .mo-dow { font-size: 9.5px; font-weight: 700; letter-spacing: 0.06em; color: var(--dim); text-align: center; }
.w-month .mo-day {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  border-radius: 6px;
}
.w-month .mo-day.out { color: #4a5363; }
.w-month .mo-day.today { background: var(--preset); color: var(--preset-text); font-weight: 700; }
.w-month .mo-day.has::after {
  content: '';
  position: absolute;
  bottom: 2px;
  width: 4px;
  height: 4px;
  border-radius: 50%;
  background: var(--warn);
}
.w-month .mo-hint:empty { display: none; }
```

Add the four css/js files to `deck.html` and `harness.html` as in Task 3 (agenda, then month).

In `harness.js` `SAMPLE`, after the `display` entry (add a comma after it):

```js
    agenda: {
      status: 'ok', total: 4,
      events: [
        { title: 'Standup', when: 'in 4 min', state: 'soon', link: true },
        { title: 'Design review', when: '16:00', state: 'later', link: true },
        { title: '1:1 with Ayşe', when: 'Tomorrow 09:00', state: 'later', link: false }
      ]
    },
    month: {
      title: 'September 2026', status: 'ok',
      days: Array.from({ length: 42 }, (_, i) => {
        const date = new Date(2026, 7, 31 + i);
        const events = [3, 10, 17, 24].includes(date.getDate()) && date.getMonth() === 8 ? ['11:00 Standup'] : [];
        return { day: date.getDate(), inMonth: date.getMonth() === 8, today: date.getMonth() === 8 && date.getDate() === 24, events };
      })
    }
```

- [ ] **Step 8: Check them in the harness**

In the harness at 1077×519, using edit mode:
1. Place **Agenda · 1×1**. 📷 It's amber, reading "AGENDA", "Standup", "in 4 min" and "click to join".
2. Remove it, then place **Agenda · 2×1**. 📷 It shows three rows: the first highlighted ("● Standup" / "in 4 min" in amber), then "● Design review" / "16:00", then "1:1 with Ayşe" / "Tomorrow 09:00".
3. Place **Month** in a free 2×2 area. If none is free, remove the mixer first. 📷 It shows "‹ September 2026 ›", a Monday-first grid starting 31, with 24 highlighted, dots on 3/10/17/24, and 31 Aug and the October days dimmed.
4. Click Done. Clicking › logs `widget:{"kind":"month","ref":null,"msg":"next"}`, and clicking the title logs `…"msg":"today"`. Clicking the agenda tile logs `…"kind":"agenda"…"msg":"press"`, and right-clicking it logs `…"msg":"next"`.
5. Run `harness.emit({type:'widget',kind:'agenda',ref:null,data:{status:'none',total:0,events:[]}})`. The tile reads "connect a calendar: tray → Calendar…".
6. The console has no errors. Reset the viewport and stop the server.

- [ ] **Step 9: Commit**

```bash
git add src/Deck.Shell tests/Deck.Shell.Tests
git commit -m "Add the agenda and month calendar widgets" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: README, then install and verify on the real deck

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Update the README**

In `README.md`:

Replace the tray line `- **Tray icon** → Edit layout, Microphones…, Shortcuts…, Start with Windows, Exit` with:

```markdown
- **Tray icon** → Edit layout, Microphones…, Shortcuts…, Calendar…, Start with Windows, Exit
```

In the tiles table, add these rows directly before the `| Empty cell | … |` row:

```markdown
| DICE | roll | switch d6 / d20 / coin |
| AGENDA | join the meeting (or open the day in Google Calendar) | next event |
| MONTH | ‹ › change month, title → back to today | — |
| Countdown | — | edit or delete it |
| NETWORK | — | — |
| DISPLAY | toggle the warm reading tint (drag the bar: brightness) | — |
```

After the "Editing the layout" section, add:

```markdown
## Calendar

Agenda and Month read your calendar through its private iCal address. In Google Calendar:
Settings → your calendar → **Integrate calendar** → **Secret address in iCal format**. Paste it
into tray → **Calendar…** (one link per line; more than one calendar is fine). The deck checks
them every 10 minutes while either tile is on the deck, and keeps the links on this PC only.

## Display

The Display tile changes brightness through the monitors' own controls (DDC/CI). All of them
move together and keep their differences. The warm tint is applied by the deck; it switches off
when the tile is removed or the deck exits.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "Document the new widgets" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 3: Install (controller, with the user)**

The session controller does these, not a subagent:
1. Back up the real config: `Copy-Item "\\localhost\c$\Users\justb\AppData\Roaming\Deck\config.json" <scratchpad>\config.backup-2.json`. From inside the Claude app, the plain `%APPDATA%` path shows a virtualised copy.
2. `taskkill /IM Deck.exe` (graceful; never `/F`), then wait for the process to exit.
3. `dotnet publish src/Deck.Shell/Deck.Shell.csproj -c Release -r win-x64 --self-contained true -o "C:\Users\justb\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\Deck\app" -nologo`. Check that `ui\widgets\month.js` and `Ical.Net.dll` are in the output.
4. Ask the user to start Deck from the Start menu.
5. With the user:
   - paste the iCal link (tray → Calendar…) and see "OK · N events"
   - place Agenda and Month and see real events
   - click to join a meeting
   - place Display, drag brightness, toggle the tint, then remove the tile and see the tint clear
   - place Network and see a sensible ping
   - add a countdown and edit it
   - roll the dice
   - check throughout that the app being typed in keeps focus
