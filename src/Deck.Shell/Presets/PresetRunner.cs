using System.Diagnostics;
using Deck.Shell.Windows;

namespace Deck.Shell.Presets;

/// <summary>
/// Replays a preset. The contract, decided before any of this was written:
///
///  - "Make it so", not "start everything". Windows already open are moved; only what's missing
///    is launched. Pressing WORK twice is safe.
///  - Anything not in the preset is never touched, moved or closed.
/// </summary>
internal sealed class PresetRunner
{
    /// <summary>Apps can take a long time to show a window; a fixed delay would be a coin flip.</summary>
    private const int LaunchTimeoutMs = 20_000;
    private const int PollIntervalMs = 400;

    public async Task<RunReport> RunAsync(Preset preset)
    {
        var report = new RunReport();
        var claimed = new HashSet<IntPtr>();
        var pending = new List<PresetEntry>();

        var live = WindowEnumerator.VisibleTopLevel();

        foreach (var entry in preset.Entries)
        {
            var match = FindMatch(live, entry, claimed);
            if (match is null)
            {
                pending.Add(entry);
                continue;
            }

            claimed.Add(match.Handle);
            if (WindowPlacer.Place(match.Handle, entry)) report.Moved++;
            else report.Failed.Add($"{entry.ProcessName}: Windows refused the move (running as admin?)");
        }

        if (pending.Count == 0) return report;

        foreach (var entry in pending)
        {
            try
            {
                Launch(entry);
                report.Launched++;
            }
            catch (Exception ex)
            {
                report.Failed.Add($"{entry.ProcessName}: {ex.Message}");
            }
        }

        await PlaceWhenTheyAppear(pending, claimed, report);
        return report;
    }

    /// <summary>
    /// Watches for windows to show up after launching. Deliberately matches on any new unclaimed
    /// window of the right executable rather than on the process we started: launching Chrome
    /// while Chrome is already running hands off to the existing process and exits immediately,
    /// so tracking our own child would find nothing.
    /// </summary>
    private static async Task PlaceWhenTheyAppear(List<PresetEntry> pending, HashSet<IntPtr> claimed, RunReport report)
    {
        var remaining = new List<PresetEntry>(pending);
        long deadline = Environment.TickCount64 + LaunchTimeoutMs;

        while (remaining.Count > 0 && Environment.TickCount64 < deadline)
        {
            await Task.Delay(PollIntervalMs);

            var live = WindowEnumerator.VisibleTopLevel();
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                var match = FindMatch(live, remaining[i], claimed);
                if (match is null) continue;

                claimed.Add(match.Handle);
                WindowPlacer.Place(match.Handle, remaining[i]);
                remaining.RemoveAt(i);
            }
        }

        foreach (var entry in remaining)
            report.Failed.Add($"{entry.ProcessName}: no window appeared within {LaunchTimeoutMs / 1000}s");
    }

    private static LiveWindow? FindMatch(List<LiveWindow> live, PresetEntry entry, HashSet<IntPtr> claimed)
    {
        var candidates = live
            .Where(w => !claimed.Contains(w.Handle))
            .Where(w => PathMatches(w.ExecutablePath, entry.ExecutablePath))
            .ToList();

        if (candidates.Count == 0) return null;

        // Prefer an exact title match; otherwise any unclaimed window of the same app. That
        // fallback is what makes a second WORK press reposition rather than duplicate.
        return candidates.FirstOrDefault(w => string.Equals(w.Title, entry.Title, StringComparison.Ordinal))
               ?? candidates[0];
    }

    private static bool PathMatches(string a, string b) =>
        !string.IsNullOrEmpty(a) &&
        !string.IsNullOrEmpty(b) &&
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void Launch(PresetEntry entry)
    {
        var info = new ProcessStartInfo
        {
            FileName = entry.ExecutablePath,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(entry.LaunchArguments))
            info.Arguments = entry.LaunchArguments;

        Process.Start(info);
    }

    /// <summary>Browser-specific flag for "open this URL in its own window".</summary>
    public static string BuildLaunchArguments(string processName, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        return processName.ToLowerInvariant() switch
        {
            "firefox" => $"-new-window \"{url}\"",
            "chrome" or "msedge" or "brave" or "opera" or "vivaldi" => $"--new-window \"{url}\"",
            _ => $"\"{url}\""
        };
    }
}
