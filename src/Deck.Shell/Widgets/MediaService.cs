using System.Windows.Threading;
using Deck.Shell.Audio;
using Deck.Shell.Media;

namespace Deck.Shell.Widgets;

/// <summary>
/// The two-second media poll behind Now Playing and the Mixer. They read the same Windows
/// sessions on the same beat, so they share one poll that runs while either is on the deck.
/// </summary>
internal sealed class MediaService : SharedService, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Task? _initialise;
    private bool _refreshing;

    public NowPlaying NowPlaying { get; } = new();

    public VolumeMixer Mixer { get; } = new();

    public event Action? Refreshed;

    public MediaService() => _timer.Tick += async (_, _) => await RefreshAsync();

    protected override void OnStart()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    protected override void OnStop() => _timer.Stop();

    public async Task RefreshAsync()
    {
        if (_refreshing) return;

        _refreshing = true;
        try
        {
            await (_initialise ??= NowPlaying.InitialiseAsync());
            await NowPlaying.RefreshAsync();
            Mixer.Refresh();
            Refreshed?.Invoke();
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        Mixer.Dispose();
    }
}
