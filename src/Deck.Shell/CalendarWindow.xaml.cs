using System.IO;
using System.Text.Json;
using System.Windows;
using Deck.Shell.Calendars;
using Deck.Shell.Config;
using Microsoft.Web.WebView2.Core;

namespace Deck.Shell;

/// <summary>
/// Where iCal links are pasted. An ordinary window, because pasting needs the keyboard. The links
/// themselves only appear in the text box; the status list below shows them masked.
/// </summary>
public partial class CalendarWindow : Window
{
    private readonly DeckConfig _config;
    private readonly CalendarService _calendar;
    private bool _checking;
    private int _waitFor;
    private bool _closed;

    internal CalendarWindow(DeckConfig config, CalendarService calendar)
    {
        InitializeComponent();
        _config = config;
        _calendar = calendar;
        _calendar.Updated += OnUpdated;
        Closed += (_, _) =>
        {
            _closed = true;
            _calendar.Updated -= OnUpdated;
        };

        // Always refresh on open when there are links, not just when nothing has fetched yet: the
        // cached entries can be up to 10 minutes stale, and showing "checking" while a fresh pass
        // runs beats silently displaying that stale status as if it were current.
        if (_config.CalendarLinks.Count > 0)
        {
            _checking = true;
            _waitFor = _calendar.Started + 1;
            _ = _calendar.RefreshAsync();
        }

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Deck", "WebView2");

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(env);

            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.WebMessageReceived += OnWebMessage;
            Web.CoreWebView2.NavigationCompleted += (_, _) => SendState();

            string path = Path.Combine(AppContext.BaseDirectory, "ui", "calendar.html");
            Web.CoreWebView2.NavigateToString(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Calendar window failed to start");
            Close();
        }
    }

    private void OnUpdated()
    {
        if (_closed) return;

        // A refresh already in flight when Save happened read the old links, so its completion
        // doesn't count; only a pass that started after Save (or later) does.
        if (_calendar.Completed >= _waitFor) _checking = false;

        SendState();
    }

    private void SendState()
    {
        if (_closed || Web.CoreWebView2 is null) return;

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "calendar",
            links = _config.CalendarLinks,
            checking = _checking,
            status = _calendar.Status
                .Where(s => _config.CalendarLinks.Contains(s.Link))
                .Select(s => new { label = CalendarService.Mask(s.Link), ok = s.Ok, message = s.Message })
        }));
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        if (message == "close")
        {
            Close();
            return;
        }

        const string savePrefix = "save:";
        if (!message.StartsWith(savePrefix, StringComparison.Ordinal)) return;

        string[]? lines;
        try
        {
            lines = JsonSerializer.Deserialize<string[]>(message[savePrefix.Length..]);
        }
        catch (JsonException)
        {
            return;
        }

        _config.CalendarLinks = (lines ?? [])
            .Select(l => l.Trim())
            .Where(IsLink)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _config.Save();

        _checking = true;
        _waitFor = _calendar.Started + 1;
        SendState();
        _ = _calendar.RefreshAsync();
    }

    // https and webcal only: http would send the secret link in clear text.
    private static bool IsLink(string line) =>
        Uri.TryCreate(line, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "webcal";
}
