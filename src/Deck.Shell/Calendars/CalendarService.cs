using System.Net.Http;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using Deck.Shell.Config;
using Deck.Shell.Widgets;
using Ical.Net.Evaluation;
using IcalCalendar = Ical.Net.Calendar;

namespace Deck.Shell.Calendars;

internal sealed record CalendarLinkStatus(string Link, bool Ok, string Message);

/// <summary>
/// The calendar behind Agenda and Month. While either is on the deck it fetches every link every
/// 10 minutes. A link that fails keeps its last good calendar, so a flaky connection shows
/// slightly old events rather than none.
/// </summary>
internal sealed class CalendarService : SharedService, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly DeckConfig _config;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private readonly Dictionary<string, IcalCalendar> _calendars = new(StringComparer.Ordinal);
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

    /// <summary>"none" with no links, "offline" when nothing could be loaded at all, otherwise "ok".</summary>
    public string Health =>
        _config.CalendarLinks.Count == 0 ? "none"
        : _fetchedOnce && _calendars.Count == 0 ? "offline"
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

    public List<CalendarEntry> Entries(DateTime fromLocal, DateTime toLocal)
    {
        var entries = new List<CalendarEntry>();

        foreach (var calendar in _calendars.Values)
        {
            try
            {
                entries.AddRange(CalendarParser.Entries(calendar, fromLocal, toLocal));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or EvaluationException)
            {
                // A rule Ical.Net can't evaluate spoils only its own calendar's answer.
            }
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

        foreach (string link in links)
        {
            try
            {
                string ics = await _http.GetStringAsync(ToHttps(link));
                var calendar = await Task.Run(() => CalendarParser.Load(ics));
                _calendars[link] = calendar;
                status.Add(new CalendarLinkStatus(link, true, $"OK · {calendar.Events.Count} events"));
            }
            catch (Exception ex)
            {
                // Every failure is the same to the user — this link didn't load — so all of them
                // become its status instead of an exception.
                status.Add(new CalendarLinkStatus(link, false, Describe(ex)));
            }
        }

        foreach (string gone in _calendars.Keys.Except(links).ToList()) _calendars.Remove(gone);

        Status = status;
        _fetchedOnce = true;
        Completed = pass;
        Updated?.Invoke();
    }

    /// <summary>Calendar apps hand out webcal:// links; they're plain https underneath.</summary>
    private static string ToHttps(string link) =>
        link.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + link["webcal://".Length..] : link;

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
