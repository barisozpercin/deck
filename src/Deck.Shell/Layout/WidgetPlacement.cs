namespace Deck.Shell.Layout;

/// <summary>
/// One widget on the deck, anchored at its top-left cell (zero-based). Ref tells presets and
/// shortcuts apart and is null for built-in widgets.
/// </summary>
internal sealed record WidgetPlacement(string Kind, string Variant, string? Ref, int Col, int Row);
