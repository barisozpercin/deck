using NAudio.CoreAudioApi;

namespace Deck.Shell.Audio;

/// <summary>
/// Owns the mute button's relationship with Windows' capture devices.
///
/// Two rules encoded here that the UI must not be able to violate:
/// unmuting restores each device's *previous* state rather than blindly opening it, and the deck
/// reports LIVE if any selected device is open — erring toward "assume you can be heard", because
/// the dangerous failure is believing you're muted when you aren't.
/// </summary>
internal sealed class MicController : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<string, MMDevice> _open = new();
    private readonly Dictionary<string, bool> _priorMute = new();

    private string[] _selected = [];

    /// <summary>Raised when any watched device changes mute state, including externally.</summary>
    public event Action? StateChanged;

    public IReadOnlyList<CaptureDevice> Devices { get; private set; } = [];

    public IReadOnlyList<string> Selected => _selected;

    /// <summary>
    /// The live MMDevice behind an id, so the room monitor can open a capture stream on a device
    /// this class already holds open for notifications. Lifetime is owned here — stop anything
    /// using the returned device before disposing this controller.
    /// </summary>
    public MMDevice? Find(string id) => _open.TryGetValue(id, out var device) ? device : null;

    public void Select(IEnumerable<string> deviceIds) => _selected = deviceIds.ToArray();

    /// <summary>Enumerates active capture endpoints and subscribes to their mute notifications.</summary>
    public IReadOnlyList<CaptureDevice> Refresh()
    {
        ReleaseDevices();

        var found = new List<CaptureDevice>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            try
            {
                var spec = new CaptureDevice(device.ID, device.FriendlyName, device.DeviceFriendlyName);
                found.Add(spec);

                // Hold the MMDevice: the notification subscription dies with it.
                _open[device.ID] = device;
                device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            }
            catch
            {
                // A device can vanish mid-enumeration (unplugged webcam). Skip it rather than
                // taking the whole deck down.
                device.Dispose();
            }
        }

        Devices = found;
        return found;
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data) => StateChanged?.Invoke();

    /// <summary>True if any selected input is currently open.</summary>
    public bool AnyLive
    {
        get
        {
            foreach (var id in _selected)
            {
                if (!_open.TryGetValue(id, out var device)) continue;
                try
                {
                    if (!device.AudioEndpointVolume.Mute) return true;
                }
                catch { /* device went away */ }
            }
            return false;
        }
    }

    public void Toggle() => SetMuted(AnyLive);

    public void SetMuted(bool muted)
    {
        foreach (var id in _selected)
        {
            if (!_open.TryGetValue(id, out var device)) continue;

            try
            {
                var volume = device.AudioEndpointVolume;

                if (muted)
                {
                    // Remember what we found so unmuting can't switch on a device the user
                    // deliberately keeps off.
                    _priorMute.TryAdd(id, volume.Mute);
                    volume.Mute = true;
                }
                else
                {
                    bool wasMutedBefore = _priorMute.TryGetValue(id, out bool prior) && prior;
                    volume.Mute = wasMutedBefore;
                    _priorMute.Remove(id);
                }
            }
            catch { /* device went away between enumeration and use */ }
        }

        StateChanged?.Invoke();
    }

    private void ReleaseDevices()
    {
        foreach (var device in _open.Values)
        {
            try { device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification; }
            catch { }
            device.Dispose();
        }
        _open.Clear();
    }

    public void Dispose()
    {
        ReleaseDevices();
        _enumerator.Dispose();
    }
}
