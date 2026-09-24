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

    /// <summary>How many rows fit: the 2×2 tile has room for six, the 2×1 for three. The master row takes one.</summary>
    private int RowLimit => Variant == "short" ? 3 : 6;

    private const string MasterPrefix = "master:";

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

        if (message.StartsWith(MasterPrefix, StringComparison.Ordinal))
        {
            ApplyMasterChange(message[MasterPrefix.Length..]);
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
    /// The rows the tile shows: the master level first, always, then the apps. Remembered apps
    /// always appear, running or not, so a level can be set for something that isn't open yet;
    /// whatever else is making sound fills the rest.
    /// </summary>
    private object[] BuildRows()
    {
        var live = Mixer.Apps.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        string[] names = Config.MixerLevels.Keys
            .Concat(Mixer.Apps.Where(a => a.Active).Select(a => a.Name))
            .Concat(Mixer.Apps.Select(a => a.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(RowLimit - 1)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        object master = new
        {
            name = "Master",
            label = "Master",
            icon = (string?)null,
            volume = (int)Math.Round(Mixer.MasterVolume * 100),
            muted = Mixer.MasterMuted,
            active = true,
            running = true,
            master = true
        };

        return names.Select(object (name) =>
        {
            bool running = live.TryGetValue(name, out var app);
            string? path = running ? app!.Path : Config.MixerAppPaths.GetValueOrDefault(name);

            var identity = AppIdentityResolver.Resolve(path);

            return new
            {
                name,
                // The system-sounds session borrows the volume mixer's icon, which also brings
                // its name; "Volume Mixer" reads as the overall level, which it isn't.
                label = name == VolumeMixer.SystemSoundsName ? "System sounds" : identity.DisplayName ?? name,
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
        }).Prepend(master).ToArray();
    }

    /// <summary>The master row's drag and mute. Not remembered: it's Windows' own setting, not the deck's.</summary>
    private void ApplyMasterChange(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("volume", out var volume) && volume.ValueKind == JsonValueKind.Number)
                Mixer.SetMasterVolume(Math.Clamp(volume.GetInt32(), 0, 100) / 100f);

            if (root.TryGetProperty("muted", out var muted) &&
                muted.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                Mixer.SetMasterMute(muted.GetBoolean());
                Mixer.Refresh();
                Push();
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            // Malformed message from the page; same reasoning as ApplyChange.
        }
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
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            // Malformed message from the page — bad JSON, a non-integer volume, a name that
            // isn't a string. Ignore it: an exception escaping here would take the deck down.
            // (The mixer calls themselves already swallow vanished-session errors.)
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
