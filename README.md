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
- **Tray icon** → Microphones…, Shortcuts…, Start with Windows, Exit
- **Start Menu** → Deck (if you exited and want it back)

Exit from the tray rather than Task Manager: the deck reserves screen space the way the taskbar
does, and a clean exit hands it back. If it ever gets killed hard and leaves a dead strip,
restarting Explorer clears it.

## The tiles

| Tile | Click | Right-click |
|---|---|---|
| Preset (e.g. WORK) | restore that window layout | delete it |
| `+` | capture the current layout as a preset | — |
| CLAUDE | jump to a session that wants you | mute/unmute its notifications |
| ANKARA | — | — |
| PLAYING | play/pause | next track |
| MUTE | mute/unmute your microphone | — |
| CAMERA | — | — |
| NOISE | arm/disarm the alert | recalibrate the limit |
| POMODORO | start/stop a block | reset the counter |
| STOPWATCH | start/stop | reset |
| MIXER row | mute that app (icon or name) | forget the app |
| MIXER bar | drag to set volume | — |

Any tile action can also be bound to a global keyboard shortcut — tray → **Shortcuts…**. The
screen is for state; the keyboard is for speed.

## Rebuilding

Debug run, for iterating:

```bash
dotnet run --project src/Deck.Shell/Deck.Shell.csproj
```

Publish over the installed copy:

```bash
dotnet publish src/Deck.Shell/Deck.Shell.csproj -c Release -r win-x64 --self-contained true -o "$env:LOCALAPPDATA\Deck\app"
```

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
