using System.Diagnostics;
using Deck.Shell.Layout;

namespace Deck.Shell.Widgets;

/// <summary>
/// Keeps the running widgets in step with the layout: starts what was placed, stops what was
/// removed, and routes page messages and hotkeys only to widgets actually on the deck. That
/// routing is what makes "in the library" mean "off".
/// </summary>
internal sealed class WidgetHost(Func<WidgetPlacement, IWidget?> create, Action<string, string?> reportFailure)
{
    private readonly Dictionary<(string Kind, string? Ref), IWidget> _running = [];

    /// <summary>
    /// Widgets whose Start threw. Kept so the page keeps showing "failed to start" after a
    /// reload, and so they aren't retried on every layout change — only once re-placed.
    /// </summary>
    private readonly HashSet<(string Kind, string? Ref)> _failed = [];

    public void Sync(IReadOnlyList<WidgetPlacement> placements)
    {
        var wanted = new Dictionary<(string Kind, string? Ref), WidgetPlacement>();
        foreach (var p in placements) wanted.TryAdd((p.Kind, p.Ref), p);

        foreach (var key in _running.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
        {
            StopQuietly(_running[key]);
            _running.Remove(key);
        }

        _failed.RemoveWhere(k => !wanted.ContainsKey(k));

        foreach (var (key, placement) in wanted)
        {
            if (_running.ContainsKey(key) || _failed.Contains(key)) continue;
            if (create(placement) is not { } widget) continue;

            widget.Variant = placement.Variant;

            try
            {
                widget.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Widget {placement.Kind} failed to start: {ex}");
                StopQuietly(widget);
                _failed.Add(key);
                reportFailure(placement.Kind, placement.Ref);
                continue;
            }

            _running[key] = widget;
            widget.Push();
        }
    }

    public void PushAll()
    {
        foreach (var widget in _running.Values) widget.Push();
        foreach (var (kind, reference) in _failed) reportFailure(kind, reference);
    }

    public bool Route(string kind, string? reference, string message) =>
        _running.TryGetValue((kind, reference), out var widget) && widget.Handle(message);

    public bool Hotkey(string action) => _running.Values.Any(w => w.HandleHotkey(action));

    public T? Find<T>() where T : class, IWidget => _running.Values.OfType<T>().FirstOrDefault();

    public void StopAll()
    {
        foreach (var widget in _running.Values) StopQuietly(widget);
        _running.Clear();
    }

    private static void StopQuietly(IWidget widget)
    {
        try
        {
            widget.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Widget {widget.Kind} failed to stop: {ex}");
        }
    }
}
