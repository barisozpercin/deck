namespace Deck.Shell.Widgets;

/// <summary>
/// The mute button. Taking it off the deck leaves the microphone exactly as it is — a layout
/// edit never changes hardware state.
/// </summary>
internal sealed class MicWidget(WidgetContext context) : WidgetBase(context, "mic")
{
    public override void Start()
    {
        Context.Mic.StateChanged += OnMicChanged;
        Context.Privacy.Changed += Push;
        Context.Privacy.Acquire();
    }

    public override void Stop()
    {
        Context.Mic.StateChanged -= OnMicChanged;
        Context.Privacy.Changed -= Push;
        Context.Privacy.Release();
    }

    public override bool Handle(string message)
    {
        if (message != "press") return false;

        Context.Mic.Toggle();
        return true;
    }

    public override bool HandleHotkey(string action)
    {
        if (action != "mute") return false;

        Context.Mic.Toggle();
        return true;
    }

    public override void Push()
    {
        var byId = Context.Mic.Devices.ToDictionary(d => d.Id);

        // Hardware names ("Focusrite USB Audio") rather than endpoint names ("Analogue 1 + 2") —
        // on a narrow tile the hardware is what tells you which physical thing is involved.
        string[] devices = Context.Config.MuteDeviceIds
            .Select(id => byId.TryGetValue(id, out var d) ? d.Hardware : "(missing device)")
            .ToArray();

        // The tile can say whether the mic is switched on, but not whether anything is actually
        // listening. The privacy poll is the missing half.
        var use = Context.Privacy.Microphone;

        Post(new
        {
            muted = !Context.Mic.AnyLive,
            devices,
            apps = use.InUse ? use.Describe() : ""
        });
    }

    /// <summary>Notifications arrive on a COM thread; the UI and WebView2 are thread-affine.</summary>
    private void OnMicChanged() => Context.Dispatcher.BeginInvoke(Push);
}
