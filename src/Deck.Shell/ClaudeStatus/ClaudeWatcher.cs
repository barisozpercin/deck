using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Deck.Shell.ClaudeStatus;

internal sealed record ClaudeSession(string Name, bool Working, int Pid);

/// <summary>
/// Reports what Claude Code is doing, read entirely from local state under ~/.claude.
///
/// Two files matter: sessions/&lt;pid&gt;.json names a live session and its transcript, and the
/// transcript's last record says who owes the next move — an "assistant" record means the turn
/// ended and it is waiting on you, a "user" record means a prompt or tool result just landed
/// and it is working.
/// </summary>
internal sealed class ClaudeWatcher
{
    /// <summary>Recent writes mean output is streaming right now, whatever the last record says.</summary>
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(8);

    /// <summary>
    /// A turn that has owed a reply for this long is almost certainly interrupted rather than
    /// thinking, and reporting it as "working" forever would make the tile useless.
    /// </summary>
    private static readonly TimeSpan WorkingCap = TimeSpan.FromMinutes(10);

    private readonly string _sessionsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions");

    private readonly string _projectsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    // Transcript paths never move, so the recursive lookup happens once per session.
    private readonly Dictionary<string, string> _transcripts = new(StringComparer.Ordinal);

    public IReadOnlyList<ClaudeSession> Sessions { get; private set; } = [];

    public void Poll()
    {
        var found = new List<ClaudeSession>();

        try
        {
            if (!Directory.Exists(_sessionsDir))
            {
                Sessions = found;
                return;
            }

            foreach (string file in Directory.EnumerateFiles(_sessionsDir, "*.json"))
            {
                var session = ReadSession(file);
                if (session is not null) found.Add(session);
            }
        }
        catch
        {
            // Claude Code rewriting these files mid-read is normal; keep the previous answer.
            return;
        }

        Sessions = found;
    }

    private ClaudeSession? ReadSession(string file)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;

            int pid = root.GetProperty("pid").GetInt32();
            string sessionId = root.GetProperty("sessionId").GetString() ?? "";
            string procStart = root.TryGetProperty("procStart", out var ps) ? ps.GetString() ?? "" : "";

            if (!IsAlive(pid, procStart)) return null;

            string name = root.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } named
                ? named
                : Path.GetFileName(root.GetProperty("cwd").GetString() ?? "session");

            return new ClaudeSession(name, IsWorking(sessionId), pid);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// PID plus process start time. A recycled PID would otherwise resurrect a session that
    /// exited hours ago — the same trap as a stale microphone entry.
    /// </summary>
    private static bool IsAlive(int pid, string procStartFileTime)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            if (!long.TryParse(procStartFileTime, out long fileTime)) return true;

            var expected = DateTime.FromFileTime(fileTime);
            return Math.Abs((process.StartTime - expected).TotalSeconds) < 5;
        }
        catch
        {
            return false;
        }
    }

    private bool IsWorking(string sessionId)
    {
        string? transcript = FindTranscript(sessionId);
        if (transcript is null) return false;

        try
        {
            var info = new FileInfo(transcript);
            TimeSpan idle = DateTime.Now - info.LastWriteTime;

            if (idle < ActiveWindow) return true;
            if (idle > WorkingCap) return false;

            var (type, stopReason) = ReadLastTurn(transcript);

            // A "user" record is a prompt or a tool result — either way Claude owes a reply.
            if (type == "user") return true;
            if (type != "assistant") return false;

            // The turn only genuinely ended if the model said so. An assistant record with
            // stop_reason "tool_use" is waiting on a tool that may run for minutes, during
            // which nothing is written to the transcript at all.
            return stopReason is not ("end_turn" or "stop_sequence" or "max_tokens");
        }
        catch
        {
            return false;
        }
    }

    private string? FindTranscript(string sessionId)
    {
        if (_transcripts.TryGetValue(sessionId, out string? cached))
            return File.Exists(cached) ? cached : null;

        try
        {
            string? match = Directory
                .EnumerateFiles(_projectsDir, sessionId + ".jsonl", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (match is not null) _transcripts[sessionId] = match;
            return match;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The last real conversation turn and how it ended.
    ///
    /// Transcripts also carry bookkeeping records — "mode", "last-prompt", "queue-operation",
    /// "attachment", "custom-title" — any of which can be the final line. Taking the literal
    /// last record would classify on those, so this scans back to the nearest user or
    /// assistant record.
    ///
    /// Only the tail is read: these files run to megabytes and this is polled continuously.
    /// FileShare.ReadWrite is required — Claude Code holds the file open for appending.
    /// </summary>
    private static (string? Type, string? StopReason) ReadLastTurn(string path)
    {
        const int TailBytes = 256 * 1024;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0) return (null, null);

        int size = (int)Math.Min(TailBytes, stream.Length);
        stream.Seek(-size, SeekOrigin.End);

        var buffer = new byte[size];
        stream.ReadExactly(buffer);

        // The first line may be a partial record from mid-chunk; it is simply skipped when
        // parsing fails.
        string[] lines = Encoding.UTF8.GetString(buffer).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            try
            {
                using var document = JsonDocument.Parse(lines[i]);
                var root = document.RootElement;

                if (!root.TryGetProperty("type", out var typeElement)) continue;
                string? type = typeElement.GetString();

                if (type != "user" && type != "assistant") continue;   // bookkeeping record

                string? stopReason = null;
                if (root.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("stop_reason", out var stop))
                {
                    stopReason = stop.GetString();
                }

                return (type, stopReason);
            }
            catch
            {
                // Truncated line; try the one before it.
            }
        }

        return (null, null);
    }
}
