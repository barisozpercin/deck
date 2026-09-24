using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using Deck.Shell.Countdowns;
using Microsoft.Web.WebView2.Core;

namespace Deck.Shell;

/// <summary>
/// Creates or edits a countdown. An ordinary focusable window, like the capture dialog: the deck
/// itself can never take keyboard input, and a name and a date both need typing.
/// </summary>
public partial class CountdownWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Countdown? _existing;

    /// <summary>The countdown as entered. It has a fresh id; when editing, the host copies its fields across.</summary>
    internal event Action<Countdown>? Saved;

    internal event Action? Deleted;

    internal CountdownWindow(Countdown? existing)
    {
        InitializeComponent();
        _existing = existing;
        Title = existing is null ? "New countdown" : "Edit countdown";
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

            string path = Path.Combine(AppContext.BaseDirectory, "ui", "countdown.html");
            Web.CoreWebView2.NavigateToString(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Countdown window failed to start");
            Close();
        }
    }

    private void SendState()
    {
        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "countdown",
            editing = _existing is not null,
            label = _existing?.Label ?? "",
            date = _existing?.Target.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            time = _existing is { HasTime: true } timed
                ? timed.Target.ToString("HH:mm", CultureInfo.InvariantCulture)
                : ""
        }));
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        switch (message)
        {
            case "cancel":
                Close();
                return;

            case "delete":
                ConfirmDelete();
                return;
        }

        const string savePrefix = "save:";
        if (!message.StartsWith(savePrefix, StringComparison.Ordinal)) return;

        SavePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SavePayload>(message[savePrefix.Length..], JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (payload is null || !CountdownInput.TryCreate(payload.Label, payload.Date, payload.Time, out var countdown))
            return;

        Saved?.Invoke(countdown);
        Close();
    }

    private void ConfirmDelete()
    {
        if (_existing is null) return;

        var answer = MessageBox.Show(this,
            $"Delete the countdown \"{_existing.Label}\"?",
            "Delete countdown", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        Deleted?.Invoke();
        Close();
    }

    private sealed record SavePayload(string? Label, string? Date, string? Time);
}
