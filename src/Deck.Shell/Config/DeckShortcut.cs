namespace Deck.Shell.Config;

/// <summary>A tile that launches something — an app, a URL, a control panel page.</summary>
internal sealed class DeckShortcut
{
    public string Label { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Arguments { get; set; }

    /// <summary>Shown under the label so a tile's target isn't a mystery.</summary>
    public string? Note { get; set; }
}
