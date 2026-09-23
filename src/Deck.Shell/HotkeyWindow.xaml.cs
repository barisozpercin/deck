using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Deck.Shell.Config;
using Deck.Shell.Hotkeys;
using Deck.Shell.Interop;
using Microsoft.Web.WebView2.Core;

namespace Deck.Shell;

public partial class HotkeyWindow : Window
{
    private readonly DeckConfig _config;
    private string? _recording;
    private IReadOnlyList<string> _conflicts = [];

    /// <summary>Raised whenever a binding changes, so the host can re-register immediately.</summary>
    internal event Action? Changed;

    internal HotkeyWindow(DeckConfig config)
    {
        InitializeComponent();
        _config = config;
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
            Web.CoreWebView2.NavigationCompleted += (_, _) => SendBindings();

            string path = Path.Combine(AppContext.BaseDirectory, "ui", "hotkeys.html");
            Web.CoreWebView2.NavigateToString(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Shortcut window failed to start");
            Close();
        }
    }

    /// <summary>Every action the deck can expose to a shortcut.</summary>
    private IEnumerable<(string Action, string Label)> Actions()
    {
        yield return ("mute", "Mute / unmute microphone");
        yield return ("room", "Arm / disarm noise alerts");
        yield return ("pomodoro", "Start / stop pomodoro block");
        yield return ("stopwatch", "Start / stop stopwatch");
        yield return ("nowplaying", "Play / pause media");

        foreach (var preset in _config.Presets)
            yield return ($"{Deck.Shell.Layout.WidgetCatalog.PresetActionPrefix}{preset.Id}", $"Run preset · {preset.Name}");
    }

    private void SendBindings()
    {
        if (Web.CoreWebView2 is null) return;

        var rows = Actions().Select(a => new
        {
            action = a.Action,
            label = a.Label,
            display = _config.Hotkeys.FirstOrDefault(h => h.Action == a.Action)?.Display ?? ""
        });

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "bindings",
            rows,
            conflicts = _conflicts
        }));
    }

    /// <summary>Called by the host after re-registering, so refusals are visible immediately.</summary>
    internal void SetConflicts(IReadOnlyList<string> conflicts)
    {
        _conflicts = conflicts;
        SendBindings();
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        if (message == "close")
        {
            Close();
            return;
        }

        if (message.StartsWith("record:", StringComparison.Ordinal))
        {
            BeginRecording(message["record:".Length..]);
            return;
        }

        if (message.StartsWith("clear:", StringComparison.Ordinal))
        {
            string action = message["clear:".Length..];
            _config.Hotkeys.RemoveAll(h => h.Action == action);
            Commit();
        }
    }

    private void BeginRecording(string action)
    {
        _recording = action;
        KeyCatcher.Visibility = Visibility.Visible;

        // Taking WPF focus pulls Win32 focus off WebView2's child window, which is the only
        // way this window sees raw key events at all.
        KeyCatcher.Focus();
        Keyboard.Focus(KeyCatcher);
    }

    private void EndRecording()
    {
        _recording = null;
        KeyCatcher.Visibility = Visibility.Collapsed;
        Web.Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_recording is null)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;

        // Alt combos arrive as Key.System with the real key in SystemKey.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (IsModifierKey(key)) return;   // still waiting for the actual key

        if (key == Key.Escape)
        {
            EndRecording();
            SendBindings();
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            _config.Hotkeys.RemoveAll(h => h.Action == _recording);
            EndRecording();
            Commit();
            return;
        }

        var modifiers = Keyboard.Modifiers;

        // A bare letter would hijack that key everywhere in Windows. Function keys are the
        // exception — they're the natural home for a dedicated deck shortcut.
        bool isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (modifiers == ModifierKeys.None && !isFunctionKey)
        {
            CatcherHint.Text = "That needs a modifier — try Ctrl, Alt or Shift with it";
            return;
        }

        string action = _recording;
        _config.Hotkeys.RemoveAll(h => h.Action == action);
        _config.Hotkeys.Add(new HotkeyBinding
        {
            Action = action,
            Modifiers = ToWin32Modifiers(modifiers),
            VirtualKey = (uint)KeyInterop.VirtualKeyFromKey(key),
            Display = Describe(modifiers, key)
        });

        EndRecording();
        CatcherHint.Text = "Esc to cancel  ·  Backspace to clear";
        Commit();
    }

    private void Commit()
    {
        _config.Save();
        Changed?.Invoke();
        SendBindings();
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;

    private static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= HotkeyNative.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= HotkeyNative.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= HotkeyNative.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= HotkeyNative.MOD_WIN;
        return result;
    }

    private static string Describe(ModifierKeys modifiers, Key key)
    {
        var parts = new StringBuilder();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Append("Ctrl + ");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Append("Alt + ");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Append("Shift + ");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Append("Win + ");
        parts.Append(key.ToString());
        return parts.ToString();
    }
}
