namespace Deck.Shell.Widgets;

/// <summary>
/// One tile's behaviour. An instance runs once — Start, then Stop. Putting a widget back on the
/// deck creates a fresh one, so nothing a removed widget was doing (a running timer, a disarmed
/// alert) comes back with it.
/// </summary>
internal interface IWidget
{
    string Kind { get; }

    /// <summary>Which preset or shortcut this tile is; null for built-in widgets.</summary>
    string? Ref { get; }

    /// <summary>The size it was placed in, from the catalogue ("standard", "short", …).</summary>
    string Variant { get; set; }

    /// <summary>Placed on the deck: start timers, polls, capture streams.</summary>
    void Start();

    /// <summary>Taken off the deck: stop everything <see cref="Start"/> began.</summary>
    void Stop();

    /// <summary>Send the tile's full current state to the page.</summary>
    void Push();

    /// <summary>A message from this tile on the page. True if it was understood.</summary>
    bool Handle(string message);

    /// <summary>A global hotkey. True if this widget owns the action.</summary>
    bool HandleHotkey(string action);
}
