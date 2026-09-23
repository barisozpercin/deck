using System.Diagnostics;
using Deck.Shell.Interop;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Deck.Shell.Audio;

internal sealed record MixerApp(string Name, string? Path, float Volume, bool Muted, bool Active);

/// <summary>
/// Per-application playback volume — the Windows volume mixer, reachable in one press.
///
/// To be clear about what this is not: it does not route audio anywhere. Splitting output into
/// separate stream/chat/personal mixes, as Wave Link does, needs a kernel-mode virtual audio
/// device, which is a different category of software entirely. This adjusts existing sessions
/// on the existing output.
/// </summary>
internal sealed class VolumeMixer : IDisposable
{
    /// <summary>Notification sounds, alerts, and anything else Windows plays itself.</summary>
    public const string SystemSoundsName = "System";

    private static readonly string SystemSoundsIconSource =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SndVol.exe");

    private readonly MMDeviceEnumerator _enumerator = new();

    public IReadOnlyList<MixerApp> Apps { get; private set; } = [];

    public void Refresh()
    {
        var byApp = new Dictionary<string, MixerApp>(StringComparer.OrdinalIgnoreCase);

        ForEachSession((session, name, path) =>
        {
            bool active = session.State == AudioSessionState.AudioSessionStateActive;

            // One row per application, not per session: Chrome alone opens several, one per
            // renderer process, and a list of eight "chrome" rows helps nobody.
            if (byApp.TryGetValue(name, out var existing))
                byApp[name] = existing with { Active = existing.Active || active };
            else
                byApp[name] = new MixerApp(name, path, session.SimpleAudioVolume.Volume,
                    session.SimpleAudioVolume.Mute, active);
        });

        Apps = byApp.Values.OrderByDescending(a => a.Active).ThenBy(a => a.Name).ToList();
    }

    public void SetVolume(string appName, float volume) =>
        ForEachSession((session, name, _) =>
        {
            if (string.Equals(name, appName, StringComparison.OrdinalIgnoreCase))
                session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
        });

    public void SetMute(string appName, bool muted) =>
        ForEachSession((session, name, _) =>
        {
            if (string.Equals(name, appName, StringComparison.OrdinalIgnoreCase))
                session.SimpleAudioVolume.Mute = muted;
        });

    private void ForEachSession(Action<AudioSessionControl, string, string?> action)
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    if (session.State == AudioSessionState.AudioSessionStateExpired) continue;

                    uint pid = session.GetProcessID;

                    // Windows reports the system-sounds session as process 0. It has no
                    // executable, so it gets a fixed name and borrows the volume mixer's icon.
                    if (pid == 0)
                    {
                        action(session, SystemSoundsName, SystemSoundsIconSource);
                        continue;
                    }

                    if (Resolve(pid) is not { } app) continue;

                    action(session, app.Name, app.Path);
                }
                catch
                {
                    // Sessions disappear as apps close; skip rather than abandoning the sweep.
                }
            }
        }
        catch
        {
            // No default output device, or the endpoint changed mid-enumeration.
        }
    }

    /// <summary>
    /// Processes that host someone else's app rather than being one. Windows Search and the
    /// Widgets panel both hold audio sessions through msedgewebview2, and showing the runtime's
    /// name and Edge logo tells the user nothing about what is making the sound.
    /// </summary>
    private static readonly HashSet<string> RuntimeHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedgewebview2",
        "DiscordHookHelper",
        "DiscordHookHelper64",
        "SpotifyLauncher",
        "ApplicationFrameHost",
        "RuntimeBroker",
        "dllhost"
    };

    private readonly Dictionary<uint, (string Name, string? Path)> _resolved = [];

    /// <summary>Walks up out of runtime hosts to the application actually responsible.</summary>
    private (string Name, string? Path)? Resolve(uint pid)
    {
        if (_resolved.TryGetValue(pid, out var cached)) return cached;

        string? name = ProcessName(pid);
        if (name is null) return null;

        int current = (int)pid;

        for (int depth = 0; depth < 5 && RuntimeHosts.Contains(name); depth++)
        {
            int parent = ProcessTree.GetParent(current);
            if (parent <= 0) break;

            string? parentName = ProcessName((uint)parent);
            if (parentName is null) break;

            current = parent;
            name = parentName;
        }

        string path = WindowNative.GetProcessPath((uint)current);
        var result = (name, string.IsNullOrEmpty(path) ? null : path);

        _resolved[pid] = result;
        return result;
    }

    private static string? ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _enumerator.Dispose();
}
