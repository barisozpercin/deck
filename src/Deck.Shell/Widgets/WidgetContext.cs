using System.Windows.Threading;
using Deck.Shell.Audio;
using Deck.Shell.Calendars;
using Deck.Shell.Config;
using Deck.Shell.Notifications;

namespace Deck.Shell.Widgets;

/// <summary>Everything a widget is allowed to use. Built once by the main window.</summary>
internal sealed class WidgetContext
{
    public required DeckConfig Config { get; init; }

    public required Notifier Notifier { get; init; }

    /// <summary>
    /// Owned by the main window, not a widget: the mic tile, the noise tile and the Microphones
    /// window all read the same devices.
    /// </summary>
    public required MicController Mic { get; init; }

    public required TickService Tick { get; init; }

    public required MediaService Media { get; init; }

    public required PrivacyService Privacy { get; init; }

    /// <summary>The iCal feeds behind Agenda and Month. Also owned by the main window, which the Calendar window needs.</summary>
    public required CalendarService Calendar { get; init; }

    /// <summary>For events that arrive off the UI thread — audio notifications, capture callbacks.</summary>
    public required Dispatcher Dispatcher { get; init; }

    /// <summary>Sends one widget's data to the page: kind, reference, data.</summary>
    public required Action<string, string?, object> Post { get; init; }
}
