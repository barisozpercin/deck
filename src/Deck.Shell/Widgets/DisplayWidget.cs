using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Deck.Shell.Display;

namespace Deck.Shell.Widgets;

/// <summary>
/// Brightness for every DDC/CI monitor, shifted together, plus the warm reading tint. The monitor
/// calls are slow, so they run on a background task that only ever applies the newest requested
/// levels — a drag never builds up a backlog of stale ones.
/// </summary>
internal sealed class DisplayWidget(WidgetContext context) : WidgetBase(context, "display")
{
    private const string BrightnessPrefix = "brightness:";

    /// <summary>How often a drag reaches the monitors. Faster just queues work inside their controllers.</summary>
    private static readonly TimeSpan ApplyInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Sleep, a display change or a game can reset gamma; putting the tint back every few seconds is cheaper than watching for all of them.</summary>
    private const int TintRefreshTicks = 10;

    private readonly object _gate = new();
    private MonitorBrightness? _monitors;
    private IReadOnlyList<MonitorLevel> _levels = [];

    /// <summary>Supported monitors' levels when the current drag began; null between drags.</summary>
    private int[]? _dragStart;

    /// <summary>The newest levels waiting to be applied, one per monitor.</summary>
    private int[]? _pending;

    private bool _applying;
    private bool _ready;
    private bool _stopped;
    private int _ticks;

    public override void Start()
    {
        Context.Tick.Ticked += OnTick;
        Context.Tick.Acquire();

        if (Context.Config.DisplayTint) GammaTint.Apply();

        _ = OpenMonitorsAsync();
    }

    public override void Stop()
    {
        _stopped = true;
        Context.Tick.Ticked -= OnTick;
        Context.Tick.Release();

        // "Removed means off": the tint goes with the tile.
        GammaTint.Reset();

        lock (_gate)
        {
            // A brightness change still in flight disposes the monitors itself when it finishes.
            if (_applying) return;
            _monitors?.Dispose();
            _monitors = null;
        }
    }

    public override bool Handle(string message)
    {
        if (message == "press")
        {
            ToggleTint();
            return true;
        }

        if (message == "brightness-commit")
        {
            _dragStart = null;
            _ = RefreshLevelsAsync();
            return true;
        }

        if (message.StartsWith(BrightnessPrefix, StringComparison.Ordinal) &&
            int.TryParse(message[BrightnessPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int target))
        {
            Drag(Math.Clamp(target, 0, 100));
            return true;
        }

        return false;
    }

    public override void Push()
    {
        var supported = _levels.Where(l => l.Supported).ToList();

        Post(new
        {
            ready = _ready,
            tint = Context.Config.DisplayTint,
            level = BrightnessShift.Average(supported.Select(l => l.Percent)),
            monitors = supported.Count,
            unsupported = _levels.Where(l => !l.Supported).Select(l => l.Name).ToArray()
        });
    }

    private async Task OpenMonitorsAsync()
    {
        MonitorBrightness monitors;
        IReadOnlyList<MonitorLevel> levels;

        try
        {
            // Enumerating asks every monitor for its range: seconds, on a bad day. Never on the UI thread.
            monitors = await Task.Run(() => new MonitorBrightness());
            levels = await Task.Run(monitors.Read);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException or InvalidOperationException)
        {
            // No DDC/CI monitors to talk to (or the Win32 wrappers can't be reached at all): show
            // "n/a" instead of leaving the tile stuck on its loading state forever.
            Debug.WriteLine($"Display widget failed to enumerate monitors: {ex}");
            _ready = true;
            Push();
            return;
        }

        if (_stopped)
        {
            monitors.Dispose();
            return;
        }

        lock (_gate) _monitors = monitors;
        _levels = levels;
        _ready = true;
        Push();
    }

    private void Drag(int target)
    {
        if (!_ready) return;

        _dragStart ??= _levels.Where(l => l.Supported).Select(l => l.Percent).ToArray();
        int[] shifted = BrightnessShift.Apply(_dragStart, target);

        // Show it at once; the monitors catch up behind.
        int next = 0;
        _levels = _levels.Select(l => l.Supported ? l with { Percent = shifted[next++] } : l).ToList();

        Queue(_levels.Select(l => l.Percent).ToArray());
        Push();
    }

    private void Queue(int[] percents)
    {
        lock (_gate)
        {
            _pending = percents;
            if (_applying) return;
            _applying = true;
        }

        _ = Task.Run(ApplyLoopAsync);
    }

    private async Task ApplyLoopAsync()
    {
        while (true)
        {
            int[]? next;
            MonitorBrightness? monitors;

            lock (_gate)
            {
                next = _pending;
                _pending = null;
                monitors = _monitors;

                if (next is null || monitors is null || _stopped)
                {
                    _applying = false;
                    if (_stopped)
                    {
                        _monitors?.Dispose();
                        _monitors = null;
                    }
                    return;
                }
            }

            monitors.Set(next);
            await Task.Delay(ApplyInterval);
        }
    }

    /// <summary>After a drag, read back what the monitors actually settled on — they round to their own steps.</summary>
    private async Task RefreshLevelsAsync()
    {
        await Task.Delay(ApplyInterval * 3);

        MonitorBrightness? monitors;
        lock (_gate) monitors = _monitors;
        if (monitors is null || _stopped || _dragStart is not null) return;

        var levels = await Task.Run(monitors.Read);
        if (_stopped || _dragStart is not null) return;

        _levels = levels;
        Push();
    }

    private void ToggleTint()
    {
        Context.Config.DisplayTint = !Context.Config.DisplayTint;
        Context.Config.Save();

        if (Context.Config.DisplayTint) GammaTint.Apply();
        else GammaTint.Reset();

        Push();
    }

    private void OnTick()
    {
        if (++_ticks % TintRefreshTicks == 0 && Context.Config.DisplayTint) GammaTint.Apply();
    }
}
