using System.Diagnostics;
using System.IO;
using Deck.Shell.Interop;
using Microsoft.Win32;

namespace Deck.Shell.Privacy;

internal sealed record CapabilityUse(bool InUse, IReadOnlyList<string> Apps)
{
    public static readonly CapabilityUse None = new(false, []);

    public string Describe() => Apps.Count == 0 ? string.Empty : string.Join(", ", Apps);
}

/// <summary>
/// Reads Windows' capability consent store to answer "is anything using the camera or the
/// microphone right now, and what?".
///
/// Read-only by design: there is no camera equivalent of the audio endpoint's mute switch, so a
/// control here would sometimes lie about having worked.
/// </summary>
internal static class CapabilityWatcher
{
    private const string StorePath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    /// <summary>
    /// The deck holds the room sensor's microphone open permanently, so without this the
    /// microphone indicator would report the deck itself, forever.
    /// </summary>
    private static readonly string OwnName =
        Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Deck.Shell");

    /// <param name="FullPath">Non-packaged apps only; null for Store apps.</param>
    /// <param name="MatchNames">Process names to look for, most specific first.</param>
    private sealed record Candidate(string Display, string? FullPath, string[] MatchNames);

    public static CapabilityUse Query(string capability)
    {
        var candidates = new List<Candidate>();
        Scan(Registry.CurrentUser, capability, candidates);
        Scan(Registry.LocalMachine, capability, candidates);

        string[] live = candidates
            .Where(c => !string.Equals(c.Display, OwnName, StringComparison.OrdinalIgnoreCase))
            .Where(IsRunning)
            .Select(c => c.Display)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return live.Length == 0 ? CapabilityUse.None : new CapabilityUse(true, live);
    }

    private static void Scan(RegistryKey root, string capability, List<Candidate> candidates)
    {
        try
        {
            using var key = root.OpenSubKey(StorePath + capability);
            if (key is not null) Walk(key, candidates, depth: 0);
        }
        catch
        {
            // A capability this machine doesn't expose, or one we can't read.
        }
    }

    private static void Walk(RegistryKey key, List<Candidate> candidates, int depth)
    {
        // The store nests at most two levels: <capability>\<packagedApp> and
        // <capability>\NonPackaged\<exe path>.
        if (depth > 2) return;

        foreach (string name in key.GetSubKeyNames())
        {
            try
            {
                using var child = key.OpenSubKey(name);
                if (child is null) continue;

                if (child.GetValue("LastUsedTimeStart") is long start &&
                    child.GetValue("LastUsedTimeStop") is long stop)
                {
                    // A zero stop time means "no stop was recorded" — which is what an app
                    // still holding the device looks like, AND what a crashed one looks like
                    // forever after. Liveness is checked separately.
                    if (start > 0 && stop == 0) candidates.Add(FromKeyName(name));
                }
                else
                {
                    Walk(child, candidates, depth + 1);
                }
            }
            catch
            {
                // Skip unreadable entries rather than losing the whole scan.
            }
        }
    }

    private static Candidate FromKeyName(string keyName)
    {
        // Non-packaged entries are full paths with '#' standing in for '\'.
        if (keyName.Contains('#'))
        {
            string path = keyName.Replace('#', '\\');
            string name = Path.GetFileNameWithoutExtension(path);
            return new Candidate(name, path, [name]);
        }

        // Packaged entries look like "Elgato.WaveLink_g54w8ztgkx496", whose process is
        // "Elgato.WaveLink", or "91750D7E.Slack_8she8kybcnzg4", whose process is "Slack".
        string token = keyName.Split('_')[0];
        int dot = token.LastIndexOf('.');
        string shortName = dot >= 0 && dot < token.Length - 1 ? token[(dot + 1)..] : token;

        return new Candidate(shortName, null, [token, shortName]);
    }

    /// <summary>
    /// The second signal, and the one that makes this trustworthy. Matching on the full image
    /// path matters: a stale entry for Discord app-1.0.9214 must not be validated by a running
    /// app-1.0.9223 — same name, different build, and the old one is long uninstalled.
    /// </summary>
    private static bool IsRunning(Candidate candidate)
    {
        foreach (string name in candidate.MatchNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            try
            {
                if (processes.Length == 0) continue;

                // Store apps give us no path to compare, so a live process of that name is
                // the best evidence available.
                if (candidate.FullPath is null) return true;

                foreach (var process in processes)
                {
                    string path = WindowNative.GetProcessPath((uint)process.Id);
                    if (string.Equals(path, candidate.FullPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }

        return false;
    }
}
