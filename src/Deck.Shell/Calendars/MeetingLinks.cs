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
