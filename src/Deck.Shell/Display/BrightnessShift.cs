namespace Deck.Shell.Display;

/// <summary>
/// "Shift together": every monitor moves by the same amount from where it was when the drag
/// began, so a deliberately dimmer monitor stays dimmer. Working from the drag's starting levels,
/// not the last applied ones, means that hitting 0 or 100 mid-drag doesn't flatten the
/// differences — dragging back restores them.
/// </summary>
internal static class BrightnessShift
{
    public static int Average(IEnumerable<int> levels)
    {
        var list = levels.ToList();
        return list.Count == 0 ? 0 : (int)Math.Round(list.Average());
    }

    public static int[] Apply(IReadOnlyList<int> start, int target)
    {
        int delta = target - Average(start);
        return start.Select(level => Math.Clamp(level + delta, 0, 100)).ToArray();
    }
}
