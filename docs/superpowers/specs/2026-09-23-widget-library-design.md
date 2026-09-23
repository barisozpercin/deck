# Widget library — design

**Date:** 2026-09-23
**Status:** approved in conversation, awaiting spec review

## Problem

Every tile on the deck has a hard-coded place in the 6×3 grid. Pomodoro and Stopwatch take up
two cells but are never used, and there is no way to take a tile off the deck, put it back later,
or pick a different size for it.

## Goal

An Android-style widget library. Any tile can come off the deck into the library and be put back
into free space later, in one of the sizes the widget offers. The first use is moving Pomodoro and
Stopwatch into the library.

## Product behaviour

### Grid

- Still 6 columns × 3 rows, same tile look.
- A widget has a fixed size per **variant** (1×1, 2×1 or 2×2). A placement is anchored at its
  top-left cell.
- At most **one instance of each built-in widget** on the deck, in any one variant. Presets and
  shortcuts are the exception: each one is its own tile, and there can be many.

### Catalogue

| Widget | Variants | Notes |
|---|---|---|
| Claude | 1×1 | as today |
| Weather | 1×1 standard, 1×1 compact, **2×1 hourly** | see below |
| Now Playing | 1×1 standard, **2×1 with art & controls** | see below |
| System (CPU/RAM/GPU) | 1×1 | as today |
| Noise | 1×1 | as today |
| Mic | 1×1 | as today |
| Camera | 1×1 | as today |
| World Clock | 1×1 | as today |
| Mixer | **2×1 (3 apps)**, 2×2 (6 apps) | as today, row limit depends on variant |
| Pomodoro | 1×1 | as today |
| Stopwatch | 1×1 | as today |
| Preset (one per saved preset) | 1×1 | as today's preset tile |
| Shortcut (one per configured shortcut) | 1×1 | as today's shortcut tile |
| **New preset** | — | library action, not a tile: opens the capture window |

New variants:

- **Weather 1×1 compact** — the icon and temperature, larger, with the condition label below. No
  feels/high/low row.
- **Weather 2×1 hourly** — the standard tile's content on the left; the next 6 hours on the right,
  each as hour · icon · temperature. It uses the same Open-Meteo request, with `hourly` added.
- **Now Playing 2×1** — album art (from the media session thumbnail; the spinning record if there
  is none), title, artist, app, and previous / play-pause / next buttons. The buttons do the
  controlling, and clicking elsewhere on the tile does nothing.
- **Mixer 2×1** — the same rows as the 2×2 mixer, limited to 3 apps. The "N more · right-click"
  hint and the pop-out mixer window still apply.

### Normal mode

- Tiles behave exactly as they do today.
- Empty cells are drawn as faint dashed cells. Clicking an empty cell enters edit mode.
- Right-clicking a preset or shortcut tile still deletes it permanently, after a confirmation, as
  it does today.

### Edit mode

Entered from **Tray → Edit layout**, or by clicking an empty cell.

- Every tile shows a **×** badge. Clicking it sends the widget to the library. It is not
  deleted, and for presets and shortcuts the saved data is kept.
- Tiles are **draggable**:
  - dropped on free space large enough for it → it moves there;
  - dropped on a tile of the **same size** → the two swap;
  - anything else → the tile goes back where it was.
- Every empty cell shows a **+**. Clicking it opens the **library panel** over the deck, and the
  clicked cell becomes the top-left anchor for whatever is picked.
- The tiles' own click, right-click and drag actions are turned off, so nothing can be muted,
  started or changed by accident while editing.
- A **Done** button, pinned to the deck's corner, leaves edit mode. Leaving edit mode is the
  only way out: the deck never takes keyboard focus, so Esc can't reach it.

### Library panel

- It covers the deck grid while open and has a close (back) button.
- It lists only what isn't on the deck: built-in widgets not placed, presets and shortcuts not
  placed, and **New preset**.
- Widgets with several variants show each variant as its own card, labelled with its size.
- A variant that doesn't fit at the clicked cell (it runs off the grid or overlaps a tile) is
  shown greyed out with "needs 2 free cells here" or "needs a 2×2 space here".
- Picking a card places the widget and closes the panel. The deck stays in edit mode.
- **New preset** opens the existing capture window. When the preset is saved it is placed at
  the clicked cell. If the window is cancelled, nothing changes.
- Right-clicking a preset or shortcut card in the library deletes it permanently, after the same
  confirmation used today.

### Removed means off

A widget that is not on the deck is fully stopped:

| Widget | Stops |
|---|---|
| Noise | the capture stream on the room sensor, the breach notification, the daily 21:00 re-arm |
| Claude | transcript polling and "Claude is waiting" notifications |
| Weather | the 15-minute fetch |
| Now Playing + Mixer | the 2-second media poll, but only once **both** are off the deck (they share it); remembered mixer levels stop being re-applied when the mixer is off |
| Mic + Camera | the privacy poll, but only once **both** are off (it feeds both tiles) |
| System | CPU/RAM/GPU sampling |
| World Clock | clock pushes |
| Pomodoro / Stopwatch | the timer is reset and stopped |

- **Hotkeys** bound to an off-deck widget, or to an off-deck preset, do nothing. The Shortcuts
  window marks them "(not on deck)" but keeps the binding, so it works again once the widget is
  back.
- **Settings are kept** (noise limit, mixer levels, Claude mute, device choices) and apply again
  when the widget returns.
- **Mic exception:** removing the Mic tile leaves the microphone's mute state as it is. The
  deck never changes hardware state because of a layout edit.

### First run after the update

The current layout is migrated as it appears today, except Pomodoro and Stopwatch:

```
row 1:  [preset/shortcut 1] [2] [3] [4]  [Claude]      [Weather std]
row 2:  [Now Playing std]  [System] [Noise] [Mic]  [Mixer 2×2 ────]
row 3:  [ empty ]  [ empty ]  [World Clock] [Camera]  [──────────────]
```

- Existing presets, then shortcuts, fill row 1 columns 1–4 in their current order. Any beyond
  four go to the library, and unused cells in that range stay empty.
- Pomodoro and Stopwatch start in the library.

## Technical design

### Widgets as units

Every tile's logic moves out of `MainWindow.xaml.cs` (≈1,200 lines today) into its own class
under `src/Deck.Shell/Widgets/`, behind one interface:

```csharp
internal interface IWidget
{
    string Kind { get; }                 // "weather", "mixer", "preset", ...
    void Start();                        // placed on the deck
    void Stop();                         // removed from the deck
    void Push();                         // send full state to the page
    bool Handle(string message);         // page → widget messages
    bool HandleHotkey(string action);    // global hotkey actions
}
```

- **Shared services** such as the media poll (Now Playing + Mixer), the privacy poll (Mic +
  Camera) and the one-second tick are reference-counted. The first widget to need a service
  starts it, and the last one to stop stops it.
- **`WidgetHost`** owns the widget instances for everything placed. It starts and stops them as
  the layout changes and routes page messages and hotkeys to the right one. Messages to a widget
  that isn't placed are dropped.
- `MainWindow` is left with window/AppBar/WebView plumbing, tray items, edit-mode messages and
  the host.

### Layout model

A pure, UI-free `DeckLayout` class in `src/Deck.Shell/Layout/`:

```csharp
record WidgetPlacement(string Kind, string Variant, string? Ref, int Col, int Row);
// Ref = preset/shortcut id; null for built-ins.
```

- Operations: `CanPlace`, `Place`, `Remove`, `Move` (including the same-size swap), `Library`
  (what's not placed), `Validate`.
- Variant sizes come from one static catalogue, the single source of truth for kinds, variants,
  sizes and labels.
- It is saved in `DeckConfig` as `Layout: List<WidgetPlacement>`, with `LayoutInitialised: bool`
  so the migration runs exactly once. Removing everything must not trigger it again.
- **Validation on load:** placements with an unknown kind or variant, a duplicate built-in, a
  missing preset/shortcut, an out-of-bounds position or an overlap are dropped. The widget ends
  up in the library, and a corrupt layout never stops the deck from starting.

### Stable ids for presets and shortcuts

Presets and shortcuts are addressed by list index today (`press:preset:0`, hotkey `preset:0`).
Indexes shift when something is deleted, so both get a persisted `Id` (GUID), assigned on load
if missing. Existing `preset:<index>` hotkey bindings are rewritten to `preset:<id>` during the
same one-time migration.

### Page

- `deck.html` no longer hard-codes tile positions. The host sends a `layout` message
  (placements + library contents + edit-mode flag), and the page builds the grid from it.
- Each variant has its own template and render function keyed by `kind/variant`.
- Messages are namespaced per widget (`w:<kind>[:<ref>]:<message>`) so they can reach the host
  without the big switch.
- Edit mode (badges, drag, + cells, library panel, Done) lives entirely in the page. It posts
  `layout-place`, `layout-remove`, `layout-move`, `edit-exit` and `library-new-preset`, the
  host applies them to `DeckLayout`, saves, and sends the new `layout` back. The host is the
  single source of truth.
- Drag uses pointer events with capture, the same technique as the mixer sliders, so it needs
  no focus.

### Error handling

- A widget that throws in `Start` is logged, shown as a tile reading "failed to start", and
  doesn't take the host down.
- Failures in the new data (hourly forecast, album art) degrade to today's content. If there's
  no art, the record is shown. If there's no hourly data, the right half reads "no forecast".

## Testing

- New xUnit project `tests/Deck.Shell.Tests` for the pure logic:
  - `DeckLayout`: fits/doesn't fit at the grid edges, overlap, a 2×2 over a 1×1, same-size swap,
    different-size swap refused, remove → appears in library, one-of-each rule.
  - Migration: today's config becomes the expected layout (including more than four
    presets/shortcuts and zero presets), preset hotkeys are rewritten to ids, and it runs only
    once.
  - Validation: every corruption case above is dropped, not thrown.
- Manual check in the running deck: enter/leave edit mode, drag, swap, place each variant,
  remove each widget and confirm it stops (e.g. the noise meter no longer captures, and the
  Claude notification doesn't fire), and confirm clicks never take focus from the foreground app.

## Out of scope

- The same widget more than once with different settings (e.g. two weather cities).
- Resizing a placed widget in place. You remove it and re-add it in the other size.
- Creating shortcuts from the UI. That still happens in the config file, as today.
- Changing the grid size.
