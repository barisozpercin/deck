using Deck.Shell.Privacy;

namespace Deck.Shell.Widgets;

/// <summary>
/// "Is anything using the camera or microphone?" — polled once a second while the Mic or Camera
/// tile is on the deck. The mic tile uses the microphone half to say what is listening.
/// </summary>
internal sealed class PrivacyService(TickService tick) : SharedService
{
    public CapabilityUse Camera { get; private set; } = CapabilityUse.None;

    public CapabilityUse Microphone { get; private set; } = CapabilityUse.None;

    /// <summary>Raised only on change: this polls every second and the page repaints on every message.</summary>
    public event Action? Changed;

    protected override void OnStart()
    {
        tick.Ticked += Poll;
        tick.Acquire();
        Poll();
    }

    protected override void OnStop()
    {
        tick.Ticked -= Poll;
        tick.Release();
    }

    private void Poll()
    {
        var camera = CapabilityWatcher.Query("webcam");
        var microphone = CapabilityWatcher.Query("microphone");

        if (Same(camera, Camera) && Same(microphone, Microphone)) return;

        Camera = camera;
        Microphone = microphone;
        Changed?.Invoke();
    }

    private static bool Same(CapabilityUse a, CapabilityUse b) =>
        a.InUse == b.InUse && a.Describe() == b.Describe();
}
