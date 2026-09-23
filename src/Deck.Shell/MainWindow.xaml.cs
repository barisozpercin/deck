using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Deck.Shell.Audio;
using Deck.Shell.Config;
using Deck.Shell.Hotkeys;
using Deck.Shell.Interop;
using Deck.Shell.Layout;
using Deck.Shell.Notifications;
using Deck.Shell.Startup;
using Deck.Shell.Widgets;
using Microsoft.Web.WebView2.Core;
using static Deck.Shell.Interop.NativeMethods;

namespace Deck.Shell;

public partial class MainWindow : Window
{
    /// <summary>Physical pixels of screen height the deck claims along the bottom edge.</summary>
    private const int DeckHeightPx = 520;

    /// <summary>
    /// The page is served from the ui folder under this made-up host name rather than injected
    /// as one string, so its stylesheet and scripts can live in their own files. ".example" is
    /// reserved and never resolves, so nothing leaves the machine.
    /// </summary>
    private const string UiHost = "deck.example";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private AppBarHost? _appBar;
    private ForegroundTracker? _tracker;
    private MicController? _mic;
    private Notifier? _notifier;
    private HotkeyManager? _hotkeys;
    private HotkeyWindow? _hotkeyWindow;
    private DeviceWindow? _deviceWindow;
    private MediaService? _media;
    private WidgetHost? _host;
    private DeckConfig _config = new();

    /// <summary>Edit mode lives here rather than in the page, because the tray can switch it on.</summary>
    private bool _editing;

    private IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += (_, _) => Cleanup();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // The whole product thesis in three flags: clicking the deck must not change which
        // window has keyboard focus, and the deck must never appear in Alt-Tab.
        long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        // Global hotkeys register against this window, which works fine despite it never
        // taking focus — that's exactly why they're worth having.
        _hotkeys = new HotkeyManager(_hwnd);
        _hotkeys.Triggered += RunAction;

        _tracker = new ForegroundTracker();

        _appBar = new AppBarHost(_hwnd);
        App.ReleaseScreenSpace = () => _appBar?.Remove();

        if (_appBar.Register())
        {
            _appBar.Dock(Monitors.PickDeckMonitor(), DeckHeightPx);
        }
        else
        {
            MessageBox.Show("Could not register the AppBar; the deck will float instead.", "Deck");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeys is not null && _hotkeys.HandleMessage(msg, wParam))
        {
            handled = true;
            return IntPtr.Zero;
        }

        // Refusing activation on click is what keeps the user's app in front. WS_EX_NOACTIVATE
        // alone is not enough once child HWNDs (WebView2's) are in the picture.
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        if (_appBar is not null && msg == unchecked((int)_appBar.CallbackMessage))
        {
            if (wParam.ToInt32() == ABN_POSCHANGED) _appBar.ReapplyPosition();
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _config = DeckConfig.Load();
        if (LayoutMigration.Prepare(_config)) _config.Save();

        _notifier = new Notifier();
        _notifier.ExitRequested += Close;

        // Turn autostart on once, then leave the decision to the tray toggle — re-enabling it
        // on every launch would silently override the user switching it off.
        if (!_config.AutoStartInitialised)
        {
            AutoStart.Set(true);
            _config.AutoStartInitialised = true;
            _config.Save();
        }
        else
        {
            AutoStart.RefreshIfEnabled();
        }

        _notifier.AddItem("Edit layout", EnterEditMode);
        _notifier.AddItem("Microphones…", OpenDevices);
        _notifier.AddItem("Shortcuts…", OpenHotkeys);
        _notifier.AddToggle("Start with Windows", AutoStart.IsEnabled, AutoStart.Set);
        _notifier.AddExitItem();

        ApplyHotkeys();
        StartAudio();
        StartWidgets();

        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Deck", "WebView2");
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(env);

            var settings = Web.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;

            Web.CoreWebView2.WebMessageReceived += OnWebMessage;
            // The page can only be told the state once its listener exists.
            Web.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                PushLayout();
                _host?.PushAll();
            };

            // Served files can be cached across runs; after an update the deck must never run
            // yesterday's scripts against today's host.
            try
            {
                await Web.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
            }
            catch
            {
                // A stale cache is better than a blank deck — fall through and navigate anyway.
            }

            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                UiHost, Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Deny);
            Web.CoreWebView2.Navigate($"https://{UiHost}/deck.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "WebView2 failed to start");
        }
    }

    /// <summary>
    /// The microphone controller lives outside the widgets: the mic tile, the noise tile and the
    /// Microphones window all read the same devices.
    /// </summary>
    private void StartAudio()
    {
        _mic = new MicController();
        var devices = _mic.Refresh();

        if (_config.IsFirstRun || _config.MuteDeviceIds.Count == 0)
        {
            _config.ApplyDefaults(devices);
            _config.Save();
        }

        _mic.Select(_config.MuteDeviceIds);
    }

    private void StartWidgets()
    {
        var tick = new TickService();
        _media = new MediaService();

        var context = new WidgetContext
        {
            Config = _config,
            Notifier = _notifier!,
            Mic = _mic!,
            Tick = tick,
            Media = _media,
            Privacy = new PrivacyService(tick),
            Dispatcher = Dispatcher,
            Post = PostWidget
        };

        _host = new WidgetHost(
            placement => WidgetFactory.Create(placement, context),
            (kind, reference) => PostWidget(kind, reference, new { failed = true }));

        _host.Sync(_config.Layout);
    }

    private void PostWidget(string kind, string? reference, object data) =>
        PostJson(new { type = "widget", kind, @ref = reference, data });

    private void PostJson(object message)
    {
        if (Web.CoreWebView2 is null) return;
        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    /// <summary>
    /// Everything the page needs to draw the grid: where each widget sits and how big it is,
    /// whether edit mode is on, and what the library panel can offer.
    /// </summary>
    private void PushLayout()
    {
        var layout = new DeckLayout(_config.Layout);

        PostJson(new
        {
            type = "layout",
            editing = _editing,
            columns = DeckLayout.Columns,
            rows = DeckLayout.Rows,
            placements = _config.Layout.Select(p =>
            {
                var size = WidgetCatalog.Find(p.Kind, p.Variant)!;
                return new
                {
                    kind = p.Kind,
                    variant = p.Variant,
                    @ref = p.Ref,
                    col = p.Col,
                    row = p.Row,
                    w = size.Width,
                    h = size.Height
                };
            }),
            library = BuildLibrary(layout)
        });
    }

    /// <summary>
    /// What the library offers: every built-in widget not on the deck, in each of its sizes,
    /// then the presets and shortcuts that aren't on it.
    /// </summary>
    private IEnumerable<object> BuildLibrary(DeckLayout layout)
    {
        foreach (var kind in layout.UnplacedBuiltIns())
            yield return LibraryItem(kind, null, kind.Title);

        var preset = WidgetCatalog.Find("preset")!;
        foreach (var p in _config.Presets.Where(p => !layout.IsPlaced("preset", p.Id)))
            yield return LibraryItem(preset, p.Id, p.Name);

        var shortcut = WidgetCatalog.Find("shortcut")!;
        foreach (var s in _config.Shortcuts.Where(s => !layout.IsPlaced("shortcut", s.Id)))
            yield return LibraryItem(shortcut, s.Id, s.Label);
    }

    private static object LibraryItem(WidgetKind kind, string? reference, string title) => new
    {
        kind = kind.Id,
        @ref = reference,
        title,
        group = kind.PerItem ? kind.Title : null,
        variants = kind.Variants.Select(v => new { variant = v.Id, label = v.Label, w = v.Width, h = v.Height })
    };

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        if (message.StartsWith("widget:", StringComparison.Ordinal))
            RouteWidgetMessage(message["widget:".Length..]);
        else if (message.StartsWith("layout:", StringComparison.Ordinal))
            HandleLayoutOp(message["layout:".Length..]);
    }

    private void RouteWidgetMessage(string json)
    {
        WidgetMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<WidgetMessage>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;   // Malformed message from the page; ignore.
        }

        if (message is { Kind: { } kind, Msg: { } msg }) _host?.Route(kind, message.Ref, msg);
    }

    private void HandleLayoutOp(string json)
    {
        LayoutOp? op;
        try
        {
            op = JsonSerializer.Deserialize<LayoutOp>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (op?.Op is null) return;

        var layout = new DeckLayout(_config.Layout);
        bool changed = false;

        switch (op.Op)
        {
            case "edit":
                _editing = true;
                break;

            case "done":
                _editing = false;
                break;

            case "place" when op.Kind is not null:
                string variant = op.Variant ?? WidgetCatalog.Find(op.Kind)?.Default.Id ?? "";
                changed = layout.Place(op.Kind, variant, op.Ref, op.Col, op.Row);
                break;

            case "remove" when op.Kind is not null:
                changed = layout.Remove(op.Kind, op.Ref);
                break;

            case "move" when op.Kind is not null:
                changed = layout.Move(op.Kind, op.Ref, op.Col, op.Row);
                break;

            case "new-preset":
                OpenCapture(op.Col, op.Row);
                return;

            case "delete" when op.Kind is not null:
                DeleteItem(op.Kind, op.Ref);
                return;
        }

        // Pushed even when nothing changed, so a refused drag snaps back to where it was.
        if (changed) CommitLayout(layout);
        else PushLayout();
    }

    private void EnterEditMode()
    {
        _editing = true;
        PushLayout();
    }

    /// <summary>
    /// Capture runs in its own ordinary window: the deck can never take keyboard focus, and
    /// naming a preset and typing URLs both need a keyboard. The new preset lands in the cell
    /// the library was opened from; if that cell has filled up meanwhile, it waits in the library.
    /// </summary>
    private void OpenCapture(int col, int row)
    {
        var capture = new CaptureWindow();
        capture.Saved += preset =>
        {
            _config.Presets.Add(preset);
            var layout = new DeckLayout(_config.Layout);
            layout.Place("preset", WidgetCatalog.Standard, preset.Id, col, row);
            CommitLayout(layout);
        };
        capture.Show();
        capture.Activate();
    }

    /// <summary>
    /// Permanently deletes a preset or shortcut, from a right-click on its tile or its library
    /// card. MessageBox rather than an in-deck confirm: a dialog inside a non-activating window
    /// can't reliably take the keyboard, and deleting should be deliberate.
    /// </summary>
    private void DeleteItem(string kind, string? reference)
    {
        if (reference is null) return;

        if (kind == "preset" && _config.Presets.FirstOrDefault(p => p.Id == reference) is { } preset)
        {
            var answer = MessageBox.Show(
                $"Delete the preset \"{preset.Name}\"?\n\nThis only removes the button. Nothing on your screen changes.",
                "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            _config.Presets.Remove(preset);
            _config.Hotkeys.RemoveAll(h => h.Action == WidgetCatalog.PresetActionPrefix + reference);
            if (_hotkeyWindow is null) ApplyHotkeys(notifyOnConflict: false);
        }
        else if (kind == "shortcut" && _config.Shortcuts.FirstOrDefault(s => s.Id == reference) is { } shortcut)
        {
            var answer = MessageBox.Show(
                $"Remove the \"{shortcut.Label}\" shortcut?",
                "Remove shortcut", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            _config.Shortcuts.Remove(shortcut);
        }
        else
        {
            return;
        }

        var layout = new DeckLayout(_config.Layout);
        layout.Remove(kind, reference);
        CommitLayout(layout);
    }

    /// <summary>Saves a changed layout, starts and stops widgets to match, and redraws the page.</summary>
    private void CommitLayout(DeckLayout layout)
    {
        _config.Layout = layout.Placements.ToList();
        _config.Save();
        _host?.Sync(_config.Layout);
        PushLayout();
    }

    private void ApplyHotkeys(bool notifyOnConflict = true)
    {
        _hotkeys?.Apply(_config.Hotkeys);

        if (notifyOnConflict && _hotkeys is { Conflicts.Count: > 0 })
            _notifier?.Show("Some shortcuts couldn't be registered", string.Join("\n", _hotkeys.Conflicts));
    }

    /// <summary>
    /// Fired by a global hotkey; window messages already arrive on the UI thread. Only widgets
    /// on the deck can answer, so a hotkey for a widget in the library does nothing.
    /// </summary>
    private void RunAction(string action) => _host?.Hotkey(action);

    /// <summary>Whether a hotkey's widget is on the deck; hotkeys for widgets in the library do nothing.</summary>
    private bool IsOnDeck(string action) =>
        WidgetCatalog.OwnerOf(action) is { } owner && new DeckLayout(_config.Layout).IsPlaced(owner.Kind, owner.Ref);

    private void ApplyDeviceConfig()
    {
        _mic?.Select(_config.MuteDeviceIds);
        _host?.Find<NoiseWidget>()?.RestartCapture();
        _host?.PushAll();
    }

    private void OpenDevices()
    {
        if (_deviceWindow is { IsVisible: true })
        {
            _deviceWindow.Activate();
            return;
        }

        _deviceWindow = new DeviceWindow(_config, _mic?.Devices ?? []);
        _deviceWindow.Changed += ApplyDeviceConfig;
        _deviceWindow.Closed += (_, _) => _deviceWindow = null;
        _deviceWindow.Show();
        _deviceWindow.Activate();
    }

    private void OpenHotkeys()
    {
        if (_hotkeyWindow is { IsVisible: true })
        {
            _hotkeyWindow.Activate();
            return;
        }

        // Registered hotkeys swallow their own combos before any window sees them, so
        // re-binding Ctrl+Alt+M would be impossible while Ctrl+Alt+M is still live.
        _hotkeys?.UnregisterAll();

        _hotkeyWindow = new HotkeyWindow(_config, IsOnDeck);

        _hotkeyWindow.Changed += () =>
        {
            // Register briefly to find out whether Windows will accept the combo, report that,
            // then stand down again so the next recording isn't intercepted.
            ApplyHotkeys(notifyOnConflict: false);
            string[] conflicts = _hotkeys?.Conflicts.ToArray() ?? [];
            _hotkeys?.UnregisterAll();
            _hotkeyWindow?.SetConflicts(conflicts);
        };

        _hotkeyWindow.Closed += (_, _) =>
        {
            _hotkeyWindow = null;
            ApplyHotkeys();
        };

        _hotkeyWindow.Show();
        _hotkeyWindow.Activate();
    }

    private void Cleanup()
    {
        App.ReleaseScreenSpace = null;

        // Widgets first: the noise tile holds a capture stream on a device the mic controller
        // owns, so it has to let go before the controller is disposed.
        _host?.StopAll();
        _media?.Dispose();
        _mic?.Dispose();

        _hotkeys?.Dispose();
        _notifier?.Dispose();
        _tracker?.Dispose();
        _appBar?.Remove();
    }

    private sealed record WidgetMessage(string? Kind, string? Ref, string? Msg);

    private sealed record LayoutOp(string? Op, string? Kind, string? Variant, string? Ref, int Col, int Row);
}
