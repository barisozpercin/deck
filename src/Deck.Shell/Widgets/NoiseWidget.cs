using System.Globalization;
using System.Windows.Threading;
using Deck.Shell.Audio;

namespace Deck.Shell.Widgets;

/// <summary>
/// The room-noise monitor. Off the deck it closes its capture stream entirely — nothing listens
/// to the room while this tile is in the library.
/// </summary>
internal sealed class NoiseWidget(WidgetContext context) : WidgetBase(context, "noise")
{
    /// <summary>
    /// A disarmed noise monitor is supposed to be temporary, but this desktop can run for weeks
    /// without a restart, so "re-arms when the deck starts" would rarely fire. This gives that
    /// rule a heartbeat.
    /// </summary>
    private const int DailyRearmHour = 21;

    private const string ThresholdPrefix = "threshold-set:";

    private RoomMonitor? _room;
    private DispatcherTimer? _rearmTimer;
    private DateTime _lastRearm = DateTime.Now.Date.AddDays(-1);
    private string? _error;

    public override void Start()
    {
        _room = new RoomMonitor { Threshold = Context.Config.RoomThreshold, Armed = true };

        // Capture callbacks arrive on an audio thread; the UI and WebView2 are thread-affine.
        var dispatcher = Context.Dispatcher;
        _room.LevelChanged += level => dispatcher.BeginInvoke(() => PushLevel(level));
        _room.Failed += message => dispatcher.BeginInvoke(() =>
        {
            _error = message;
            Push();
        });
        _room.Breached += () => dispatcher.BeginInvoke(() =>
            Context.Notifier.Show("Keep it down 🤫", "The room is over your limit."));

        RestartCapture();

        _rearmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _rearmTimer.Tick += (_, _) => RearmIfDue();
        _rearmTimer.Start();
    }

    public override void Stop()
    {
        _rearmTimer?.Stop();
        _rearmTimer = null;
        _room?.Dispose();
        _room = null;
    }

    /// <summary>(Re)opens the capture stream on whichever device is currently the room sensor.</summary>
    public void RestartCapture()
    {
        if (_room is null) return;

        _room.Stop();
        _error = null;

        if (Context.Config.RoomSensorDeviceId is not { } id)
        {
            _error = "no room sensor selected";
            return;
        }

        var device = Context.Mic.Find(id);
        if (device is null)
        {
            _error = "room sensor not found";
            return;
        }

        _room.Start(device);
    }

    public override bool Handle(string message)
    {
        if (_room is null) return false;

        if (message.StartsWith(ThresholdPrefix, StringComparison.Ordinal))
        {
            SetThreshold(message[ThresholdPrefix.Length..]);
            return true;
        }

        switch (message)
        {
            case "press":
                ToggleArmed();
                return true;

            case "calibrate":
                Context.Config.RoomThreshold = _room.Calibrate();
                Context.Config.Save();
                Push();
                return true;

            case "threshold-commit":
                Context.Config.Save();
                Push();
                return true;

            default:
                return false;
        }
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "room" || _room is null) return false;

        ToggleArmed();
        return true;
    }

    public override void Push() => Post(new
    {
        armed = _room?.Armed ?? false,
        running = _room?.IsRunning ?? false,
        threshold = Math.Round(Context.Config.RoomThreshold),
        error = _error,
        device = DeviceName()
    });

    private void PushLevel(double level)
    {
        // A callback queued just before the tile was removed.
        if (_room is null) return;

        Post(new { level = Math.Round(level, 1) });
    }

    private string? DeviceName() =>
        Context.Config.RoomSensorDeviceId is { } id
            ? Context.Mic.Devices.FirstOrDefault(d => d.Id == id)?.Hardware
            : null;

    private void ToggleArmed()
    {
        if (_room is null) return;

        _room.Armed = !_room.Armed;
        Push();
    }

    /// <summary>
    /// Live threshold updates while the handle is being dragged: applied at once so the meter's
    /// over/under colouring tracks the mouse, but only written to disk on threshold-commit.
    /// </summary>
    private void SetThreshold(string raw)
    {
        if (_room is null) return;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return;

        value = Math.Clamp(value, 0, 100);
        _room.Threshold = value;
        Context.Config.RoomThreshold = value;
    }

    private void RearmIfDue()
    {
        var now = DateTime.Now;
        var todayAt = now.Date.AddHours(DailyRearmHour);

        if (now < todayAt || _lastRearm >= todayAt) return;

        _lastRearm = todayAt;
        if (_room is { Armed: false })
        {
            _room.Armed = true;
            Push();
        }
    }
}
