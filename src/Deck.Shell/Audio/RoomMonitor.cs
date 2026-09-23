using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Deck.Shell.Audio;

/// <summary>
/// Ambient room-loudness monitor — a port of github.com/barisozpercin/hush, constants and all.
///
/// This watches the ROOM, not the user's voice, which is why it listens on a device deliberately
/// excluded from the mute button: muting at night would otherwise blind it at exactly the moment
/// neighbours matter.
/// </summary>
internal sealed class RoomMonitor : IDisposable
{
    // Ported verbatim from Hush's renderer.js so behaviour matches the app it replaces.
    private const double DbFloor = -60.0;
    private const double DbCeil = 0.0;
    private const int SustainMs = 350;
    private const int CooldownMs = 6000;
    private const double Smoothing = 0.3;
    private const int UiIntervalMs = 50;   // 20fps is plenty for a meter

    private WasapiCapture? _capture;
    private double _smoothed;
    private long _overSince = -1;
    private long _lastNotified = -1;
    private long _lastUiPush;

    /// <summary>0–100. Above this, sustained, fires <see cref="Breached"/>.</summary>
    public double Threshold { get; set; } = 60;

    /// <summary>
    /// Disarming silences alerts but never stops the meter. Not persisted: the deck always
    /// starts armed, so a disarm can't quietly outlive the reason for it.
    /// </summary>
    public bool Armed { get; set; } = true;

    public double Level => _smoothed;
    public bool IsRunning => _capture is not null;

    public event Action<double>? LevelChanged;
    public event Action? Breached;
    public event Action<string>? Failed;

    public void Start(MMDevice device)
    {
        Stop();
        try
        {
            _capture = new WasapiCapture(device);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) Failed?.Invoke(e.Exception.Message);
            };
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            _capture = null;
            Failed?.Invoke(ex.Message);
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format is null) return;

        double rms = Rms(e.Buffer, e.BytesRecorded, format);

        double db = 20.0 * Math.Log10(rms <= 0 ? 1e-7 : rms);
        db = Math.Clamp(db, DbFloor, DbCeil);
        double level = (db - DbFloor) / (DbCeil - DbFloor) * 100.0;

        _smoothed += (level - _smoothed) * Smoothing;

        long now = Environment.TickCount64;
        Evaluate(now);

        if (now - _lastUiPush >= UiIntervalMs)
        {
            _lastUiPush = now;
            LevelChanged?.Invoke(_smoothed);
        }
    }

    /// <summary>
    /// Sustain stops a single door slam tripping the alert; cooldown stops a noisy hour
    /// producing a stream of notifications.
    /// </summary>
    private void Evaluate(long now)
    {
        if (_smoothed > Threshold)
        {
            if (_overSince < 0)
            {
                _overSince = now;
                return;
            }

            if (now - _overSince < SustainMs) return;
            if (!Armed) return;
            if (_lastNotified >= 0 && now - _lastNotified < CooldownMs) return;

            _lastNotified = now;
            Breached?.Invoke();
        }
        else
        {
            _overSince = -1;
        }
    }

    /// <summary>Hush's "Set to my level": put the line just above whatever the room is doing now.</summary>
    public double Calibrate()
    {
        Threshold = Math.Clamp(Math.Round(_smoothed) + 12, 20, 95);
        return Threshold;
    }

    private static double Rms(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        double sum = 0;
        int count = 0;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= bytesRecorded; i += 4)
            {
                float s = BitConverter.ToSingle(buffer, i);
                sum += s * s;
                count++;
            }
        }
        else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            for (int i = 0; i + 2 <= bytesRecorded; i += 2)
            {
                float s = BitConverter.ToInt16(buffer, i) / 32768f;
                sum += s * s;
                count++;
            }
        }
        else
        {
            return 0;   // unexpected shared-mode format; meter reads silent rather than lying
        }

        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }

    public void Stop()
    {
        if (_capture is null) return;
        try
        {
            _capture.StopRecording();
            _capture.Dispose();
        }
        catch { }
        _capture = null;
    }

    public void Dispose() => Stop();
}
