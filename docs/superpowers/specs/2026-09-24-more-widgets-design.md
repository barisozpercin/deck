# More widgets — design

**Date:** 2026-09-24
**Status:** approved in conversation
**Builds on:** `2026-09-23-widget-library-design.md` (the layout, library, edit mode and the
"removed means off" rule all apply unchanged)

## Goal

Six new widgets for the library: Dice, Agenda, Month, Countdown, Network and Display, plus the
calendar connection that Agenda and Month share.

## Catalogue additions

| Kind | Variants | Per item? |
|---|---|---|
| `dice` | `standard` 1×1 | no |
| `agenda` | `standard` 1×1 (next event), `wide` 2×1 (next 3) | no |
| `month` | `standard` 2×2 | no |
| `countdown` | `standard` 1×1 | **yes**: one tile per saved countdown, like presets |
| `network` | `standard` 1×1 | no |
| `display` | `standard` 1×1 | no |

Agenda and Month are separate kinds so both can be on the deck at once.

## Dice

- Click rolls. The face tumbles for ~0.9 s, flickering through values, then lands. The result
  stays until the next roll, including across layout changes (the host remembers it for the
  session).
- Right-click cycles the mode: **d6** (pips) → **d20** (number) → **coin** (Heads/Tails). The
  mode is saved in config.
- The roll uses the page's `crypto.getRandomValues`, so there is no host round trip before the
  animation starts. The result is then sent to the host (`rolled:<value>`) so it survives a
  re-render.

## Calendar connection

- Tray → **Calendar…** opens an ordinary window. You paste one or more iCal links, one per
  line, and Save. It shows each link's status (OK · N events, or the error).
- Links are stored in `config.json`. They are only ever fetched from their own server, and
  never logged or shown on the deck.
- `webcal://` is accepted and fetched as `https://`.
- Refreshed every 10 minutes while Agenda or Month is on the deck (a shared, reference-counted
  service). A link that fails keeps its last good events. The tiles say "calendar offline"
  when every link has failed and nothing is cached, and "connect a calendar: tray →
  Calendar…" when there are no links.
- Parsed with **Ical.Net 5**, which handles repeating events, skipped instances, moved
  instances and time zones. This was verified against a sample calendar before planning.
- Events are converted to local time. All-day events keep their date, with no time.

## Agenda

- Shows timed events that haven't ended yet, looking up to 7 days ahead, soonest first. All-day
  events are left out and appear on Month instead.
- **1×1:** the focused event's title, when it is, and whether it has a join link.
- **2×1:** the next three events as rows, with the focused one highlighted.
- "When" text:
  - in progress → `now · until 11:30`
  - starts within 60 min → `in 12 min`
  - later today → `14:00`
  - tomorrow → `Tomorrow 09:00`
  - later → `Fri 09:00`
- Colours: amber when the event starts within 5 minutes, green while it's in progress.
- **Click** → opens the focused event's meeting link (Google Meet, Zoom, Teams or Webex, found in
  the location, description or Google's conference field). With no link, it opens Google
  Calendar on that day.
- **Right-click** → moves focus to the following event, wrapping around. Focus resets to the
  first event when the list changes.
- Nothing upcoming → "nothing in the next 7 days".

## Month

- A 2×2 month grid. Weeks start on Monday. Today is highlighted, and days outside the month are
  dimmed. A dot marks days with any event (timed or all-day), and hovering a day lists its
  events as a tooltip.
- Header: `‹  September 2026  ›`. The arrows change month, and clicking the title returns to
  today's month.
- Without a calendar connected, it still shows the month (no dots) plus a small hint.

## Countdown

- The library gets a **New countdown** card, next to New preset. It opens an ordinary window
  with a name, a date and an optional time (Save / Cancel). The new countdown is placed in the
  cell the library was opened from.
- **Right-click a countdown tile** → the same window in edit mode, with a **Delete** button
  (confirmation first). × still just sends the tile to the library, and right-clicking its
  library card deletes it (confirmation first), the same as presets.
- Tile: name (upper-case label), big value, and the target date below
  (`Fri 12 Dec 2026 · 18:00`).
- Values:
  - **Date only:** 2+ days away → `42 days`, 1 → `tomorrow`, 0 → `today!`, after → `+12 days`
    (and `+1 day`).
  - **With a time:** 2+ calendar days away → `42 days`; under 48 hours → `31h 12m`; after →
    `+5h 12m` for the first day, then `+12 days`.
- Colours: amber under 2 days, green on the day or within the first day after, dim once it's
  more than a day past.
- Updates on the shared one-second tick, pushing only when the text changes.

## Network

- Pings `1.1.1.1` (Cloudflare, an IP, so no DNS lookup) every 2 seconds with a 1-second
  timeout. That small ping is the only traffic it creates. The speeds shown are what the PC is
  using right now, summed over all active non-loopback adapters. It never runs a speed test.
- Tile: `NETWORK`, the ping in ms, and `↓ 12.4 Mb/s  ↑ 0.8 Mb/s`.
- States:
  - **good:** ping < 120 ms
  - **slow** (amber): ping ≥ 120 ms, or 1 lost ping
  - **bad** (red): 2 or more lost pings in a row, shown as `no reply`
  - **offline** (red): no adapter is up

## Display

- **Drag across the tile** changes brightness on every monitor that supports DDC/CI (all three
  of the user's monitors do). They **shift together**: each moves by the same amount from where
  it was when the drag started, clamped to 0–100, so their differences are kept. The tile shows
  the average.
- Brightness calls are slow (tens of milliseconds per monitor), so they run off the UI thread.
  Only the latest value is applied while dragging (at most every 150 ms), plus once on release.
- **Click** toggles a warm reading tint on every display through the display gamma ramp:
  red ×1.0, green ×0.85, blue ×0.65. Windows accepted ramps up to much stronger than this on
  the user's displays. The on/off state is saved in config and re-applied when the widget
  starts.
- While the tint is on it is re-applied every 10 seconds, because display changes and sleep can
  reset gamma. It is removed when the tint is switched off, when the widget leaves the deck,
  and when the deck exits or crashes (the app's release hook resets gamma).
- Monitors without DDC/CI are listed on the tile ("brightness not supported on X"). The tint
  still works on them.

## Host and page contract

- New page → host widget messages:
  - dice: `rolled:<v>`, `mode`
  - agenda: `press`, `next`
  - month: `prev`, `next`, `today`
  - display: `brightness:<0-100>`, `brightness-commit`, `press`
- New layout ops:
  - `new-countdown` (col, row) → opens the countdown window in create mode
  - `edit-item` (kind, ref) → opens it in edit mode; `countdown` only for now
  - `delete` also accepts `countdown`
- Validation of per-item kinds is generalised: `DeckLayout.Validate` takes an
  `itemExists(kind, ref)` check instead of preset/shortcut id sets, so countdowns are validated
  the same way.

## Testing

- **Unit tests** cover:
  - countdown text: every case above, including date-only vs timed, and singular/plural
  - agenda "when" text
  - month grid: Monday start, 6-row layout, today, leading and trailing days
  - meeting-link detection: Meet, Zoom, Teams, Webex, none, link in each field
  - calendar parsing: a fixture with a weekly event, EXDATE, a moved instance, an all-day
    event, a UTC event and a TZID event, checking local times and all-day handling
  - network assessment and rate formatting
  - generalised layout validation
- **Harness:** sample data for every new kind, and visual checks of each tile and the countdown
  flow.
- **Manual, on the real deck after install:**
  - paste the real iCal link and see events
  - join a meeting
  - drag brightness
  - toggle the tint and check it clears when the tile is removed
  - check network under normal use
  - check focus is never stolen

## Out of scope

- Adjustable tint warmth (a single fixed level for now).
- Calendar sign-in (OAuth), writing events, or reminders and notifications from the calendar.
- A real internet speed test.
- Per-monitor brightness control.
