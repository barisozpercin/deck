namespace Deck.Shell.Layout;

/// <summary>
/// The rules for where widgets may sit: inside the 6×3 grid, never overlapping, and each
/// built-in widget at most once. Pure logic with no UI, so every rule is unit-tested. The page
/// only ever asks for a change; this decides.
/// </summary>
internal sealed class DeckLayout
{
    public const int Columns = 6;
    public const int Rows = 3;

    private readonly List<WidgetPlacement> _placements;

    public DeckLayout(IEnumerable<WidgetPlacement>? placements = null) =>
        _placements = placements?.ToList() ?? [];

    public IReadOnlyList<WidgetPlacement> Placements => _placements;

    public WidgetPlacement? Find(string kind, string? reference) =>
        _placements.FirstOrDefault(p => Is(p, kind, reference));

    public bool IsPlaced(string kind, string? reference) => Find(kind, reference) is not null;

    /// <summary>The widget covering a cell, whichever of its cells that is.</summary>
    public WidgetPlacement? At(int col, int row) => _placements.FirstOrDefault(p => Covers(p, col, row));

    public bool CanPlace(string kind, string variant, string? reference, int col, int row) =>
        WidgetCatalog.Find(kind) is { } info
        && info.PerItem == (reference is not null)
        && !IsPlaced(kind, reference)
        && Fits(kind, variant, col, row, ignore: null);

    public bool Place(string kind, string variant, string? reference, int col, int row)
    {
        if (!CanPlace(kind, variant, reference, col, row)) return false;

        _placements.Add(new WidgetPlacement(kind, variant, reference, col, row));
        return true;
    }

    public bool Remove(string kind, string? reference) =>
        _placements.RemoveAll(p => Is(p, kind, reference)) > 0;

    /// <summary>
    /// Moves a widget so its top-left lands on the given cell. If it doesn't fit there but the
    /// cell belongs to a widget of exactly the same size, the two trade places instead.
    /// </summary>
    public bool Move(string kind, string? reference, int col, int row)
    {
        if (Find(kind, reference) is not { } moving) return false;
        if (moving.Col == col && moving.Row == row) return false;

        if (Fits(moving.Kind, moving.Variant, col, row, ignore: moving))
        {
            Replace(moving, moving with { Col = col, Row = row });
            return true;
        }

        if (At(col, row) is not { } other || other == moving) return false;
        if (Size(other) != Size(moving)) return false;

        Replace(moving, moving with { Col = other.Col, Row = other.Row });
        Replace(other, other with { Col = moving.Col, Row = moving.Row });
        return true;
    }

    /// <summary>
    /// Drops every placement the deck can't honour: an unknown kind or size, a second copy of a
    /// built-in, a per-item tile whose item no longer exists, anything off the grid or
    /// overlapping an earlier tile. Dropped widgets simply end up in the library, so a damaged
    /// config never stops the deck starting. Returns how many were dropped.
    /// </summary>
    /// <param name="itemExists">Asked once per per-item tile, with its kind and reference.</param>
    public int Validate(Func<string, string, bool> itemExists)
    {
        var kept = new DeckLayout();

        foreach (var p in _placements)
        {
            bool referenceExists = WidgetCatalog.Find(p.Kind) is not { PerItem: true }
                || (p.Ref is not null && itemExists(p.Kind, p.Ref));

            if (referenceExists) kept.Place(p.Kind, p.Variant, p.Ref, p.Col, p.Row);
        }

        int dropped = _placements.Count - kept._placements.Count;
        _placements.Clear();
        _placements.AddRange(kept._placements);
        return dropped;
    }

    /// <summary>Built-in widgets not currently on the deck, in catalogue order.</summary>
    public IEnumerable<WidgetKind> UnplacedBuiltIns() =>
        WidgetCatalog.Kinds.Where(k => !k.PerItem && !IsPlaced(k.Id, null));

    private bool Fits(string kind, string variant, int col, int row, WidgetPlacement? ignore)
    {
        if (WidgetCatalog.Find(kind, variant) is not { } size) return false;
        if (col < 0 || row < 0 || col + size.Width > Columns || row + size.Height > Rows) return false;

        for (int c = col; c < col + size.Width; c++)
        {
            for (int r = row; r < row + size.Height; r++)
            {
                var occupant = At(c, r);
                if (occupant is not null && occupant != ignore) return false;
            }
        }

        return true;
    }

    private static bool Covers(WidgetPlacement p, int col, int row)
    {
        var (width, height) = Size(p);
        return col >= p.Col && col < p.Col + width && row >= p.Row && row < p.Row + height;
    }

    private static (int Width, int Height) Size(WidgetPlacement p) =>
        WidgetCatalog.Find(p.Kind, p.Variant) is { } v ? (v.Width, v.Height) : (1, 1);

    private static bool Is(WidgetPlacement p, string kind, string? reference) =>
        p.Kind == kind && string.Equals(p.Ref, reference, StringComparison.Ordinal);

    private void Replace(WidgetPlacement from, WidgetPlacement to) =>
        _placements[_placements.IndexOf(from)] = to;
}
