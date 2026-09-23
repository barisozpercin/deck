using System.IO;
using System.Text.Json;
using System.Windows;
using Deck.Shell.Audio;
using Deck.Shell.Config;
using Microsoft.Web.WebView2.Core;

namespace Deck.Shell;

/// <summary>
/// Lets the user override the first-run guess about which inputs are microphones and which one
/// watches the room. Another focusable window for the same reason as the others: the deck itself
/// can never take input focus.
/// </summary>
public partial class DeviceWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly DeckConfig _config;
    private readonly IReadOnlyList<CaptureDevice> _devices;

    internal event Action? Changed;

    internal DeviceWindow(DeckConfig config, IReadOnlyList<CaptureDevice> devices)
    {
        InitializeComponent();
        _config = config;
        _devices = devices;
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
            Web.CoreWebView2.NavigationCompleted += (_, _) => SendDevices();

            string path = Path.Combine(AppContext.BaseDirectory, "ui", "devices.html");
            Web.CoreWebView2.NavigateToString(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Microphone window failed to start");
            Close();
        }
    }

    private void SendDevices()
    {
        if (Web.CoreWebView2 is null) return;

        var rows = _devices.Select(d => new
        {
            id = d.Id,
            display = d.Display,
            hardware = d.Hardware,
            isVirtual = d.IsVirtual,
            muted = _config.MuteDeviceIds.Contains(d.Id),
            isRoom = _config.RoomSensorDeviceId == d.Id
        });

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "devices", rows }));
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        if (message == "close")
        {
            Close();
            return;
        }

        const string prefix = "set:";
        if (!message.StartsWith(prefix, StringComparison.Ordinal)) return;

        try
        {
            var payload = JsonSerializer.Deserialize<SelectionPayload>(message[prefix.Length..], JsonOptions);
            if (payload is null) return;

            // Only trust ids we actually enumerated, so a stale page can't write junk to config.
            var known = _devices.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

            _config.MuteDeviceIds = payload.MuteIds.Where(known.Contains).ToList();
            _config.RoomSensorDeviceId = payload.RoomId is not null && known.Contains(payload.RoomId)
                ? payload.RoomId
                : null;

            _config.Save();
            Changed?.Invoke();
            SendDevices();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save microphone settings");
        }
    }

    private sealed record SelectionPayload(List<string> MuteIds, string? RoomId);
}
