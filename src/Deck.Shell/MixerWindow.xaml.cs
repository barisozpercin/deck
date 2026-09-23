using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Deck.Shell.Audio;
using Microsoft.Web.WebView2.Core;

namespace Deck.Shell;

public partial class MixerWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly VolumeMixer _mixer;
    private DispatcherTimer? _refresh;

    /// <summary>True while the user is dragging, so a refresh can't yank the slider back.</summary>
    private bool _editing;

    internal MixerWindow(VolumeMixer mixer)
    {
        InitializeComponent();
        _mixer = mixer;
        Loaded += OnLoaded;
        Closed += (_, _) => _refresh?.Stop();
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
            Web.CoreWebView2.NavigationCompleted += (_, _) => SendApps();

            string path = Path.Combine(AppContext.BaseDirectory, "ui", "mixer.html");
            Web.CoreWebView2.NavigateToString(File.ReadAllText(path));

            // Apps come and go while the window is open; keep the list current.
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refresh.Tick += (_, _) => SendApps();
            _refresh.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Mixer failed to start");
            Close();
        }
    }

    private void SendApps()
    {
        if (Web.CoreWebView2 is null || _editing) return;

        _mixer.Refresh();

        var rows = _mixer.Apps.Select(a => new
        {
            name = a.Name,
            volume = (int)Math.Round(a.Volume * 100),
            muted = a.Muted,
            active = a.Active
        });

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "apps", rows }));
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        switch (message)
        {
            case "close":
                Close();
                return;

            case "editing:on":
                _editing = true;
                return;

            case "editing:off":
                _editing = false;
                return;
        }

        const string prefix = "set:";
        if (!message.StartsWith(prefix, StringComparison.Ordinal)) return;

        try
        {
            var change = JsonSerializer.Deserialize<Change>(message[prefix.Length..], JsonOptions);
            if (change is null || string.IsNullOrWhiteSpace(change.Name)) return;

            if (change.Muted is { } muted) _mixer.SetMute(change.Name, muted);
            if (change.Volume is { } volume) _mixer.SetVolume(change.Name, volume / 100f);
        }
        catch
        {
            // Malformed message; ignore rather than tearing down the window.
        }
    }

    private sealed record Change(string Name, int? Volume, bool? Muted);
}
