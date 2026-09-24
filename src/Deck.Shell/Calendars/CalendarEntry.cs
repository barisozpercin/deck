namespace Deck.Shell.Calendars;

/// <summary>One occurrence of an event, in local time. All-day entries run from local midnight of their first day.</summary>
internal sealed record CalendarEntry(string Title, DateTime Start, DateTime End, bool AllDay, string? JoinUrl);
