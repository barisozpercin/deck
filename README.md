# Deck

A software control deck for Windows — a Stream Deck with no hardware, docked to the bottom of
a monitor.

Its defining property, and the reason for most of the odd decisions inside, is that **clicking
it never steals keyboard focus**. Whatever app you were using stays in front. Without that it
would be a launcher, not a deck.

## Running it

It starts with Windows and lives in the tray. Nothing else is needed day to day.

- **Installed to** `%LOCALAPPDATA%\Deck\app`
- **Settings** in `%APPDATA%\Deck\config.json`
- **Tray icon** → Edit layout, Microphones…, Shortcuts…, Calendar…, Start with Windows, Exit
- **Start Menu** → Deck (if you exited and want it back)

Exit from the tray rather than Task Manager: the deck reserves screen space the way the taskbar
does, and a clean exit hands it back. If it ever gets killed hard and leaves a dead strip,
restarting Explorer clears it.

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
| DICE | roll | switch d6 / d20 / coin |
| AGENDA | join the meeting (or open the day in Google Calendar) | next event |
| MONTH | ‹ › change month, title → back to today | — |
| Countdown | — | edit or delete it |
| NETWORK | — | — |
| DISPLAY | toggle the warm reading tint (drag the bar: brightness) | — |
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

## Calendar

Agenda and Month read your calendar through its private iCal address. In Google Calendar:
Settings → your calendar → **Integrate calendar** → **Secret address in iCal format**. Paste it
into tray → **Calendar…** (one link per line; more than one calendar is fine; https only). The
deck checks them every 10 minutes while either tile is on the deck, and keeps the links on this
PC only.

## Display

The Display tile changes brightness through the monitors' own controls (DDC/CI). All of them
move together and keep their differences. The warm tint is applied by the deck; it switches off
when the tile is removed or the deck exits, and each screen gets its original colours back.

## Rebuilding

Debug run, for iterating:

```bash
dotnet run --project src/Deck.Shell/Deck.Shell.csproj
```

Publish over the installed copy:

```bash
dotnet publish src/Deck.Shell/Deck.Shell.csproj -c Release -r win-x64 --self-contained true -o "$env:LOCALAPPDATA\Deck\app"
```

Tests (the layout rules, the migration from older configs, the widget host):

```bash
dotnet test tests/Deck.Shell.Tests/Deck.Shell.Tests.csproj
```

To work on the page without replacing the running deck, open `src/Deck.Shell/ui/dev/harness.html`
in a browser. It fakes the host with sample data.

Stop the running deck first — the executable will be locked otherwise. Autostart re-points
itself to wherever the executable actually is each time it starts, so moving the install
folder is safe.

It publishes **self-contained** (~180 MB) so it doesn't depend on the .NET runtime staying
installed on the machine.

## Things it deliberately does not do

- **Route audio between devices.** The mixer sets per-app volume on the existing output. Virtual
  outputs — the Wave Link trick — need a kernel-mode audio driver.
- **Equalise sound.** Same reason: that requires being inside the audio pipeline.
- **Report CPU temperature.** Needs a signed kernel driver and admin rights. GPU temperature is
  free via `nvml.dll`, so that one is available.
- **Show Claude usage limits.** They live server-side; nothing on disk holds them.
