using System.IO;
using System.Text.Json;
using System.Windows;
using Deck.Shell.Presets;
using Deck.Shell.Windows;
using Microsoft.Web.WebView2.Core;

namespace Deck.Shell;

/// <summary>
/// The capture dialog. This is a normal, focusable window rather than part of the deck strip
/// because the deck is deliberately WS_EX_NOACTIVATE — it can never receive keyboard input, and
/// naming a preset and typing URLs both require typing.
/// </summary>
public partial class CaptureWindow : Window
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi"
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private List<LiveWindow> _windows = [];

    internal event Action<Preset>? Saved;

    public CaptureWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Snapshot before the dialog can affect anything: the user's desk is what it was when
        // they pressed the button.
        _windows = WindowEnumerator.VisibleTopLevel();

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
            Web.CoreWebView2.NavigationCompleted += (_, _) => SendWindowList();

            string path = Path.Combine(AppContext.BaseDirectory, "ui", "capture.html");
            Web.CoreWebView2.NavigateToString(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Capture window failed to start");
            Close();
        }
    }

    private void SendWindowList()
    {
        var rows = _windows.Select((w, i) => new
        {
            index = i,
            app = w.ProcessName,
            title = w.Title,
            monitor = ShortMonitorName(w.MonitorDevice),
            maximized = w.IsMaximized,
            isBrowser = Browsers.Contains(w.ProcessName),
            launchable = !string.IsNullOrEmpty(w.ExecutablePath)
        });

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "windows", rows }));
    }

    private static string ShortMonitorName(string device) =>
        device.Replace(@"\\.\", string.Empty, StringComparison.Ordinal);

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        if (message == "cancel")
        {
            Close();
            return;
        }

        const string savePrefix = "save:";
        if (!message.StartsWith(savePrefix, StringComparison.Ordinal)) return;

        try
        {
            var payload = JsonSerializer.Deserialize<SavePayload>(message[savePrefix.Length..], JsonOptions);
            if (payload is null) return;

            Saved?.Invoke(BuildPreset(payload));
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save preset");
        }
    }

    private Preset BuildPreset(SavePayload payload)
    {
        var preset = new Preset
        {
            Name = string.IsNullOrWhiteSpace(payload.Name) ? "PRESET" : payload.Name.Trim().ToUpperInvariant()
        };

        foreach (var item in payload.Items)
        {
            if (item.Index < 0 || item.Index >= _windows.Count) continue;
            var w = _windows[item.Index];

            preset.Entries.Add(new PresetEntry
            {
                ExecutablePath = w.ExecutablePath,
                ProcessName = w.ProcessName,
                Title = w.Title,
                LaunchArguments = string.IsNullOrWhiteSpace(item.Url)
                    ? null
                    : PresetRunner.BuildLaunchArguments(w.ProcessName, item.Url!.Trim()),
                Left = w.Bounds.left,
                Top = w.Bounds.top,
                Right = w.Bounds.right,
                Bottom = w.Bounds.bottom,
                IsMaximized = w.IsMaximized
            });
        }

        return preset;
    }

    private sealed record SavePayload(string Name, List<SaveItem> Items);
    private sealed record SaveItem(int Index, string? Url);
}
