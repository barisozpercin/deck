using System.Net.Http;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using Deck.Shell.Config;
using Deck.Shell.Widgets;

namespace Deck.Shell.Calendars;

internal sealed record CalendarLinkStatus(string Link, bool Ok, string Message);

/// <summary>
/// The calendar behind Agenda and Month. While either is on the deck it fetches every link every
/// 10 minutes, expanding each into entries for the fixed horizon right there, off the UI thread.
/// A link that fails keeps its last good entries, so a flaky connection shows slightly old events
/// rather than none.
/// </summary>
internal sealed class CalendarService : SharedService, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The most one link's expansion may run before this refresh gives up on it and moves to the
    /// next link, so a feed whose repeats hang the library in some way pruning doesn't already
    /// catch can never stop every other link from refreshing.
    /// </summary>
    private static readonly TimeSpan ExpandTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The fixed window every refresh expands into. Month can page back and forward up to a year
    /// from today; Agenda only needs the next 7 days, comfortably inside it. Expanding once per
    /// refresh, for this whole span, is what lets <see cref="Entries"/> be a cheap filter on the
    /// UI thread instead of an Ical.Net call.
    /// </summary>
    private const int HorizonMonthsBack = 2;

    private const int HorizonMonthsForward = 12;

    private readonly DeckConfig _config;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private readonly Dictionary<string, IReadOnlyList<CalendarEntry>> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// SHA-256 hashes of ics text whose expansion has already timed out once. Ical.Net 5.2.3 gives
    /// no way to cancel an expansion already running, so a timed-out background task keeps its
    /// thread running forever with nothing this class can do to stop it. Checking this set before
    /// expanding is what keeps a stuck link from abandoning a fresh thread on every refresh: the
    /// same bytes are skipped straight to "too complex to read" instead of being retried, and only
    /// a link whose content actually changes (a different hash) is expanded again.
    /// </summary>
    private readonly HashSet<string> _stuckContentHashes = new(StringComparer.Ordinal);

    private bool _refreshing;
    private bool _refreshAgain;
    private bool _fetchedOnce;

    public CalendarService(DeckConfig config)
    {
        _config = config;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public IReadOnlyList<CalendarLinkStatus> Status { get; private set; } = [];

    /// <summary>Incremented at the start of every refresh pass, including a queued one that reruns after a Save.</summary>
    public int Started { get; private set; }

    /// <summary>Set to the pass number that finished, just before <see cref="Updated"/> fires.</summary>
    public int Completed { get; private set; }

    /// <summary>Raised on the UI thread after every refresh, successful or not.</summary>
    public event Action? Updated;

    /// <summary>"none" with no supported (https/webcal) link, "offline" when nothing could be loaded at all, otherwise "ok".</summary>
    public string Health =>
        !_config.CalendarLinks.Any(IsSupportedLink) ? "none"
        : _fetchedOnce && _entries.Count == 0 ? "offline"
        : "ok";

    protected override void OnStart()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    protected override void OnStop() => _timer.Stop();

    /// <summary>Safe to call at any time. A call during a refresh runs another one straight after, so a Save is never lost.</summary>
    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                await RefreshOnceAsync();
            }
            while (_refreshAgain);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>A cheap filter over each link's already-expanded entries — no Ical.Net call on this (the UI) thread.</summary>
    public List<CalendarEntry> Entries(DateTime fromLocal, DateTime toLocal)
    {
        var entries = new List<CalendarEntry>();

        foreach (var list in _entries.Values)
        {
            entries.AddRange(list.Where(e => e.Start < toLocal && e.End > fromLocal));
        }

        return entries.OrderBy(e => e.Start).ToList();
    }

    /// <summary>
    /// "calendar.google.com · #a1b2": the host, plus the first 4 hex characters of a SHA-256 of
    /// the full link. Every Google link ends in "/basic.ics", so the path alone can't tell two of
    /// them apart; the hash is stable per link, distinguishes them, and reveals nothing about it.
    /// </summary>
    public static string Mask(string link)
    {
        if (!Uri.TryCreate(ToHttps(link), UriKind.Absolute, out var uri)) return "(unreadable link)";

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(link));
        string hex = Convert.ToHexString(hash)[..4].ToLowerInvariant();
        return $"{uri.Host} · #{hex}";
    }

    public void Dispose()
    {
        _timer.Stop();
        _http.Dispose();
    }

    private async Task RefreshOnceAsync()
    {
        int pass = ++Started;
        var links = _config.CalendarLinks.ToList();
        var status = new List<CalendarLinkStatus>();
        var (from, to) = Horizon();

        foreach (string link in links)
        {
            if (!IsSupportedLink(link))
            {
                status.Add(new CalendarLinkStatus(link, false, "use an https link"));
                continue;
            }

            try
            {
                string ics = await _http.GetStringAsync(ToHttps(link));
                string contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ics)));

                if (_stuckContentHashes.Contains(contentHash))
                {
                    status.Add(new CalendarLinkStatus(link, false, "too complex to read"));
                    continue;
                }

                var expand = Task.Run(() => CalendarParser.Expand(ics, from, to));
                var finished = await Task.WhenAny(expand, Task.Delay(ExpandTimeout));

                if (finished != expand)
                {
                    // The Task.Run above is still out there, running on a thread pool thread we
                    // can't reach — see _stuckContentHashes for why remembering this content is
                    // what stops that from happening again on the next refresh.
                    _stuckContentHashes.Add(contentHash);
                    status.Add(new CalendarLinkStatus(link, false, "too complex to read"));
                    continue;
                }

                var (entries, truncated) = await expand;
                _entries[link] = entries;

                int upcoming = entries.Count(e => e.End > DateTime.Now);
                string message = $"OK · {upcoming} upcoming" + (truncated ? " · some repeats skipped" : "");
                status.Add(new CalendarLinkStatus(link, true, message));
            }
            catch (Exception ex)
            {
                // Every failure is the same to the user — this link didn't load — so all of them
                // become its status instead of an exception.
                status.Add(new CalendarLinkStatus(link, false, Describe(ex)));
            }
        }

        foreach (string gone in _entries.Keys.Except(links).ToList()) _entries.Remove(gone);

        Status = status;
        _fetchedOnce = true;
        Completed = pass;
        Updated?.Invoke();
    }

    /// <summary>The horizon this refresh expands every link into; see the constants above for why. Exposed for tests.</summary>
    internal static (DateTime From, DateTime To) Horizon()
    {
        var startOfThisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var from = startOfThisMonth.AddMonths(-HorizonMonthsBack);

        // Exclusive: midnight at the start of the month after the horizon, so every moment of the
        // horizon's own last calendar day is still inside it. A `to` of that day's own midnight
        // (the old `.AddDays(-1)`) would drop every event on that day, since CalendarParser.Entries
        // excludes anything starting at or after `to`.
        var to = startOfThisMonth.AddMonths(HorizonMonthsForward + 1);

        return (from, to);
    }

    /// <summary>Calendar apps hand out webcal:// links; they're plain https underneath.</summary>
    private static string ToHttps(string link) =>
        link.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + link["webcal://".Length..] : link;

    // https and webcal only: a hand-edited http:// link in config.json would send the secret in clear text.
    // Shared with CalendarWindow, which applies the same rule when saving a pasted link.
    internal static bool IsSupportedLink(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "webcal";

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => $"the server said {(int)code}",
        HttpRequestException => "couldn't reach the server",
        TaskCanceledException => "timed out",
        SerializationException or FormatException => "that isn't an iCal calendar",
        UriFormatException or InvalidOperationException => "that isn't a valid link",
        _ => ex.Message
    };
}
