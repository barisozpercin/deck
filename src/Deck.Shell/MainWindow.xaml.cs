using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Deck.Shell.Apps;
using Deck.Shell.Audio;
using Deck.Shell.ClaudeStatus;
using Deck.Shell.Clock;
using Deck.Shell.Config;
using Deck.Shell.Hotkeys;
using Deck.Shell.Interop;
using Deck.Shell.Media;
using Deck.Shell.Notifications;
using Deck.Shell.Presets;
using Deck.Shell.Privacy;
using Deck.Shell.Startup;
using Deck.Shell.Stats;
using Deck.Shell.Timers;
using Deck.Shell.Weather;
using Microsoft.Web.WebView2.Core;
using static Deck.Shell.Interop.NativeMethods;

namespace Deck.Shell;

public partial class MainWindow : Window
{
    /// <summary>Physical pixels of screen height the deck claims along the bottom edge.</summary>
    private const int DeckHeightPx = 520;

    /// <summary>
    /// A disarmed noise monitor is supposed to be temporary, but this desktop can run for weeks
    /// without a restart, so "re-arms when the deck starts" would rarely fire. This gives that
    /// rule a heartbeat.
    /// </summary>
    private const int DailyRearmHour = 21;

    /// <summary>How many apps the 2x2 mixer tile can show without crowding.</summary>
    private const int MixerRowLimit = 6;

    private AppBarHost? _appBar;
    private ForegroundTracker? _tracker;
    private MicController? _mic;
    private RoomMonitor? _room;
    private Notifier? _notifier;
    private HotkeyManager? _hotkeys;
    private HotkeyWindow? _hotkeyWindow;
    private DeviceWindow? _deviceWindow;
    private ClaudeWatcher? _claude;
    private DispatcherTimer? _claudeTimer;
    private readonly HashSet<string> _previouslyWaiting = new(StringComparer.Ordinal);
    private int _claudeFocusIndex;
    private bool _claudePolling;
    private bool _claudeFirstPoll = true;
    private NowPlaying? _nowPlaying;
    private VolumeMixer? _mixer;
    private MixerWindow? _mixerWindow;
    private DispatcherTimer? _mediaTimer;
    private readonly HashSet<string> _audioAppsSeen = new(StringComparer.OrdinalIgnoreCase);
    private WeatherService? _weather;
    private DispatcherTimer? _weatherTimer;
    private SystemStats? _system;
    private GpuStats? _gpu;
    private PomodoroTimer? _pomodoro;
    private StopwatchTimer? _stopwatch;
    private DispatcherTimer? _tickTimer;
    private DispatcherTimer? _rearmTimer;
    private DateTime _lastRearm = DateTime.Now.Date.AddDays(-1);
    private string? _roomError;
    private DeckConfig _config = new();
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

        _notifier.AddItem("Microphones…", OpenDevices);
        _notifier.AddItem("Shortcuts…", OpenHotkeys);
        _notifier.AddToggle("Start with Windows", AutoStart.IsEnabled, AutoStart.Set);
        _notifier.AddExitItem();

        ApplyHotkeys();

        StartAudio();
        StartTimers();
        StartMedia();
        StartWeather();
        StartClaudeWatcher();
        StartRearmTimer();

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
                PushState();
                PushTimers();
                PushPrivacy();
                PushWeather();
                PushClaude();
                PushMedia();
            };

            string path = Path.Combine(AppContext.BaseDirectory, "ui", "deck.html");
            Web.CoreWebView2.NavigateToString(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "WebView2 failed to start");
        }
    }

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

        // Notifications arrive on a COM thread; the UI and WebView2 are thread-affine.
        _mic.StateChanged += () => Dispatcher.BeginInvoke(PushState);

        StartRoomMonitor();
    }

    private void StartRoomMonitor()
    {
        _room = new RoomMonitor { Threshold = _config.RoomThreshold, Armed = true };
        _room.LevelChanged += level => Dispatcher.BeginInvoke(() => PushLevel(level));
        _room.Failed += message => Dispatcher.BeginInvoke(() =>
        {
            _roomError = message;
            PushState();
        });
        _room.Breached += () => Dispatcher.BeginInvoke(() =>
            _notifier?.Show("Keep it down 🤫", "The room is over your limit."));

        StartRoomCapture();
    }

    /// <summary>(Re)opens the capture stream on whichever device is currently the room sensor.</summary>
    private void StartRoomCapture()
    {
        if (_room is null) return;

        _room.Stop();
        _roomError = null;

        if (_config.RoomSensorDeviceId is not { } id)
        {
            _roomError = "no room sensor selected";
            return;
        }

        var device = _mic?.Find(id);
        if (device is null)
        {
            _roomError = "room sensor not found";
            return;
        }

        _room.Start(device);
    }

    private void ApplyDeviceConfig()
    {
        _mic?.Select(_config.MuteDeviceIds);
        StartRoomCapture();
        PushState();
    }

    private void StartTimers()
    {
        _system = new SystemStats();
        _gpu = new GpuStats();

        _pomodoro = new PomodoroTimer();
        _pomodoro.Changed += PushTimers;
        _pomodoro.Alert += message =>
        {
            // Played directly rather than leaning on the notification's own sound, which Focus
            // Assist and fullscreen games can suppress. A timer you don't hear is not a timer.
            SystemSounds.Exclamation.Play();
            _notifier?.Show("Pomodoro", message);
            PushTimers();
        };

        _stopwatch = new StopwatchTimer();
        _stopwatch.Changed += PushTimers;

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) =>
        {
            _pomodoro.Tick();
            if (_pomodoro.Phase != PomodoroPhase.Idle || _stopwatch.IsRunning) PushTimers();
            PollPrivacy();
            SampleStats();
            PushClock();
        };
        _tickTimer.Start();
    }

    private async void StartMedia()
    {
        _nowPlaying = new NowPlaying();
        _mixer = new VolumeMixer();

        await _nowPlaying.InitialiseAsync();

        _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _mediaTimer.Tick += async (_, _) => await RefreshMediaAsync();
        _mediaTimer.Start();

        await RefreshMediaAsync();
    }

    private async Task RefreshMediaAsync()
    {
        if (_nowPlaying is null || _mixer is null) return;

        await _nowPlaying.RefreshAsync();
        _mixer.Refresh();
        RestoreRememberedLevels();
        PushMedia();
    }

    /// <summary>
    /// Applies a remembered level when an app's audio session first appears — the Wave Link
    /// behaviour of a source keeping its level across restarts.
    ///
    /// Deliberately only on appearance, never continuously: re-asserting every tick would fight
    /// the user if they changed a volume anywhere else in Windows.
    /// </summary>
    private void RestoreRememberedLevels()
    {
        if (_mixer is null) return;

        foreach (var app in _mixer.Apps)
        {
            // Remember where the app lives so its icon still resolves when it isn't running.
            if (app.Path is not null) _config.MixerAppPaths[app.Name] = app.Path;

            if (_audioAppsSeen.Contains(app.Name)) continue;

            if (_config.MixerLevels.TryGetValue(app.Name, out int level))
                _mixer.SetVolume(app.Name, level / 100f);
        }

        _audioAppsSeen.Clear();
        foreach (var app in _mixer.Apps) _audioAppsSeen.Add(app.Name);
    }

    private void PushMedia()
    {
        if (Web.CoreWebView2 is null || _nowPlaying is null || _mixer is null) return;

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "media",
            hasSession = _nowPlaying.HasSession,
            playing = _nowPlaying.IsPlaying,
            title = _nowPlaying.Title,
            artist = _nowPlaying.Artist,
            app = _nowPlaying.App,
            mixerApps = _mixer.Apps.Count,
            mixerActive = _mixer.Apps.Count(a => a.Active),
            mixerRows = BuildMixerRows()
        }));
    }

    /// <summary>
    /// The three rows the deck shows. Remembered apps always appear, running or not, so a level
    /// can be set for something that isn't open yet; whatever else is currently making sound
    /// fills the remaining slots.
    /// </summary>
    private object[] BuildMixerRows()
    {
        if (_mixer is null) return [];

        var live = _mixer.Apps.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        string[] names = _config.MixerLevels.Keys
            .Concat(_mixer.Apps.Where(a => a.Active).Select(a => a.Name))
            .Concat(_mixer.Apps.Select(a => a.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MixerRowLimit)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return names.Select(object (name) =>
        {
            bool running = live.TryGetValue(name, out var app);
            string? path = running ? app!.Path : _config.MixerAppPaths.GetValueOrDefault(name);

            var identity = AppIdentityResolver.Resolve(path);

            return new
            {
                name,
                label = identity.DisplayName ?? name,
                icon = identity.IconDataUri,
                // A running app's real volume is the truth; a remembered one falls back to
                // whatever level was stored for its next launch.
                volume = running
                    ? (int)Math.Round(app!.Volume * 100)
                    : _config.MixerLevels.GetValueOrDefault(name, 100),
                muted = running && app!.Muted,
                active = running && app!.Active,
                running
            };
        }).ToArray();
    }

    /// <summary>
    /// Drops a remembered app from the mixer. Needed because a remembered app is shown whether
    /// or not it's running, so an uninstalled one would otherwise sit there forever.
    /// </summary>
    private void ForgetMixerApp(string name)
    {
        if (!_config.MixerLevels.Remove(name) & !_config.MixerAppPaths.Remove(name)) return;

        _config.Save();
        _mixer?.Refresh();
        PushMedia();
    }

    /// <summary>
    /// Inline mixer edits. Volume and mute arrive on the same message so a drag and a mute
    /// press can't race each other into two different refreshes.
    /// </summary>
    private void ApplyMixerChange(string json)
    {
        if (_mixer is null) return;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("name", out var nameElement)) return;
            string? name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name)) return;

            if (root.TryGetProperty("volume", out var volume) && volume.ValueKind == JsonValueKind.Number)
            {
                int level = Math.Clamp(volume.GetInt32(), 0, 100);

                // Remember it whether or not the app is running — that's the whole point.
                _config.MixerLevels[name] = level;
                _mixer.SetVolume(name, level / 100f);
            }

            if (root.TryGetProperty("muted", out var muted) &&
                muted.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _mixer.SetMute(name, muted.GetBoolean());
                // Mute is a discrete press, so reflect it immediately rather than on the next tick.
                _mixer.Refresh();
                PushMedia();
            }
        }
        catch
        {
            // Malformed message from the page; ignore.
        }
    }

    private void OpenMixer()
    {
        if (_mixer is null) return;

        if (_mixerWindow is { IsVisible: true })
        {
            _mixerWindow.Activate();
            return;
        }

        _mixerWindow = new MixerWindow(_mixer);
        _mixerWindow.Closed += (_, _) => _mixerWindow = null;
        _mixerWindow.Show();
        _mixerWindow.Activate();
    }

    private void StartClaudeWatcher()
    {
        _claude = new ClaudeWatcher();

        // Two seconds: fast enough to notice a turn ending, slow enough that tailing several
        // multi-megabyte transcripts costs nothing worth measuring.
        _claudeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _claudeTimer.Tick += (_, _) => _ = PollClaudeAsync();
        _claudeTimer.Start();

        _ = PollClaudeAsync();
    }

    private async Task PollClaudeAsync()
    {
        if (_claude is null || _claudePolling) return;

        _claudePolling = true;
        try
        {
            // Reads the disk — the first pass of the day walks today's whole transcripts, so
            // it must not run on the UI thread.
            await Task.Run(_claude.Poll);

            NotifyNewlyWaiting();
            PushClaude();
        }
        finally
        {
            _claudePolling = false;
        }
    }

    /// <summary>
    /// The tile only helps if you look at it, and you're usually looking at another monitor —
    /// so a session becoming your problem is worth a notification.
    /// </summary>
    private void NotifyNewlyWaiting()
    {
        if (_claude is null) return;

        var waiting = _claude.Sessions
            .Where(s => !s.Working)
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Don't announce everything that happens to be idle when the deck starts.
        if (!_claudeFirstPoll && _config.ClaudeNotifications)
        {
            foreach (string name in waiting.Where(n => !_previouslyWaiting.Contains(n)))
                _notifier?.Show("Claude is waiting", $"{name} finished and wants your review.");
        }

        // The seen-set is updated even while muted, so unmuting doesn't dump a backlog of
        // notifications for sessions that went quiet an hour ago.
        _claudeFirstPoll = false;
        _previouslyWaiting.Clear();
        foreach (string name in waiting) _previouslyWaiting.Add(name);
    }

    /// <summary>Repeated presses cycle, so two waiting sessions are both reachable.</summary>
    private void FocusNextClaudeSession()
    {
        if (_claude is null) return;

        // false sorts before true, so sessions waiting on you come first.
        var ordered = _claude.Sessions.OrderBy(s => s.Working).ToArray();
        if (ordered.Length == 0) return;

        var target = ordered[_claudeFocusIndex % ordered.Length];
        _claudeFocusIndex++;

        if (!WindowFocus.FocusProcessWindow(target.Pid))
            _notifier?.Show("Couldn't switch", $"No window found for {target.Name}.");
    }

    private void PushClaude()
    {
        if (Web.CoreWebView2 is null || _claude is null) return;

        var sessions = _claude.Sessions;
        var waiting = sessions.Where(s => !s.Working).ToArray();
        var working = sessions.Where(s => s.Working).ToArray();

        // Waiting sessions are named first: that's the state that needs you to do something.
        string names = string.Join(" · ", waiting.Concat(working).Select(s => s.Name));

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "claude",
            waiting = waiting.Length,
            working = working.Length,
            names,
            notify = _config.ClaudeNotifications
        }));
    }

    private void StartWeather()
    {
        _weather = new WeatherService();

        // Weather moves slowly and the service is free — 15 minutes is plenty and stays polite.
        _weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
        _weatherTimer.Start();

        _ = RefreshWeatherAsync();
    }

    private async Task RefreshWeatherAsync()
    {
        if (_weather is null) return;

        await _weather.RefreshAsync();
        PushWeather();
    }

    private void PushWeather()
    {
        if (Web.CoreWebView2 is null || _weather is null) return;

        var reading = _weather.Latest;
        var (icon, label) = reading is null
            ? ("🌡️", "—")
            : WeatherService.Describe(reading.Code);

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "weather",
            available = reading is not null,
            icon,
            label,
            temp = reading is null ? "–" : $"{Math.Round(reading.TempC)}°",
            high = reading is null ? "" : $"{Math.Round(reading.HighC)}°",
            low = reading is null ? "" : $"{Math.Round(reading.LowC)}°",
            feels = reading is null ? "" : $"{Math.Round(reading.FeelsC)}°",
            // Stale is surfaced rather than hidden: a cached number shown as current is the
            // same lying-tile problem as a mute button that didn't mute.
            stale = _weather.IsStale,
            error = _weather.Error
        }));
    }

    private void StartRearmTimer()
    {
        _rearmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _rearmTimer.Tick += (_, _) =>
        {
            var now = DateTime.Now;
            var todayAt = now.Date.AddHours(DailyRearmHour);

            if (now < todayAt || _lastRearm >= todayAt) return;

            _lastRearm = todayAt;
            if (_room is { Armed: false })
            {
                _room.Armed = true;
                PushState();
            }
        };
        _rearmTimer.Start();
    }

    private const string ThresholdPrefix = "threshold-set:";

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString() ?? string.Empty;

        if (message.StartsWith(ThresholdPrefix, StringComparison.Ordinal))
        {
            SetThreshold(message[ThresholdPrefix.Length..]);
            return;
        }

        if (message.StartsWith("mixer-set:", StringComparison.Ordinal))
        {
            ApplyMixerChange(message["mixer-set:".Length..]);
            return;
        }

        if (message.StartsWith("mixer-forget:", StringComparison.Ordinal))
        {
            ForgetMixerApp(message["mixer-forget:".Length..]);
            return;
        }

        if (message == "mixer-commit")
        {
            // Saved on release rather than on every pixel of a drag.
            _config.Save();
            return;
        }

        if (TryIndexed(message, "press:preset:", out int runIndex))
        {
            _ = RunPresetAsync(runIndex);
            return;
        }

        if (TryIndexed(message, "preset-delete:", out int deleteIndex))
        {
            DeletePreset(deleteIndex);
            return;
        }

        if (TryIndexed(message, "press:shortcut:", out int shortcutIndex))
        {
            RunShortcut(shortcutIndex);
            return;
        }

        if (TryIndexed(message, "shortcut-delete:", out int shortcutDelete))
        {
            DeleteShortcut(shortcutDelete);
            return;
        }

        switch (message)
        {
            case "press:capture":
                OpenCapture();
                break;

            case "press:claude":
                FocusNextClaudeSession();
                break;

            case "claude-notify-toggle":
                _config.ClaudeNotifications = !_config.ClaudeNotifications;
                _config.Save();
                PushClaude();
                break;

            case "press:nowplaying":
                _ = _nowPlaying?.TogglePlayPauseAsync();
                break;

            case "nowplaying-next":
                _ = _nowPlaying?.SkipNextAsync();
                break;

            case "press:mixer":
                OpenMixer();
                break;

            case "threshold-commit":
                _config.Save();
                PushState();
                break;

            case "press:mute":
                _mic?.Toggle();
                break;

            case "press:room":
                if (_room is not null)
                {
                    _room.Armed = !_room.Armed;
                    PushState();
                }
                break;

            case "press:room-calibrate":
                if (_room is not null)
                {
                    _config.RoomThreshold = _room.Calibrate();
                    _config.Save();
                    PushState();
                }
                break;

            case "press:pomodoro":
                _pomodoro?.Toggle();
                break;

            case "pomodoro-reset":
                _pomodoro?.ResetSession();
                break;

            case "press:stopwatch":
                _stopwatch?.Toggle();
                break;

            case "stopwatch-reset":
                _stopwatch?.Reset();
                break;

            case "press:exit":
                Close();
                break;
        }
    }

    private void ApplyHotkeys(bool notifyOnConflict = true)
    {
        _hotkeys?.Apply(_config.Hotkeys);

        if (notifyOnConflict && _hotkeys is { Conflicts.Count: > 0 })
            _notifier?.Show("Some shortcuts couldn't be registered", string.Join("\n", _hotkeys.Conflicts));
    }

    /// <summary>Fired by a global hotkey. Window messages already arrive on the UI thread.</summary>
    private void RunAction(string action)
    {
        switch (action)
        {
            case "mute":
                _mic?.Toggle();
                break;

            case "room":
                if (_room is not null)
                {
                    _room.Armed = !_room.Armed;
                    PushState();
                }
                break;

            case "pomodoro":
                _pomodoro?.Toggle();
                break;

            case "stopwatch":
                _stopwatch?.Toggle();
                break;

            case "nowplaying":
                _ = _nowPlaying?.TogglePlayPauseAsync();
                break;

            default:
                if (TryIndexed(action, "preset:", out int index)) _ = RunPresetAsync(index);
                break;
        }
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

        _hotkeyWindow = new HotkeyWindow(_config);

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

    private static bool TryIndexed(string message, string prefix, out int index)
    {
        index = -1;
        return message.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(message[prefix.Length..], out index);
    }

    /// <summary>
    /// Capture runs in its own ordinary window: the deck can never take keyboard focus, and
    /// naming a preset and typing URLs both need a keyboard.
    /// </summary>
    private void OpenCapture()
    {
        var capture = new CaptureWindow();
        capture.Saved += preset =>
        {
            _config.Presets.Add(preset);
            _config.Save();
            PushState();
        };
        capture.Show();
        capture.Activate();
    }

    private async Task RunPresetAsync(int index)
    {
        if (index < 0 || index >= _config.Presets.Count) return;
        var preset = _config.Presets[index];

        PushPreset(index, "running", null);

        try
        {
            var report = await new PresetRunner().RunAsync(preset);
            PushPreset(index, "done", report.Summary());

            if (report.Failed.Count > 0)
            {
                _notifier?.Show($"{preset.Name}: {report.Failed.Count} didn't work",
                    string.Join("\n", report.Failed.Take(4)));
            }
        }
        catch (Exception ex)
        {
            PushPreset(index, "error", ex.Message);
        }
    }

    private void RunShortcut(int index)
    {
        if (index < 0 || index >= _config.Shortcuts.Count) return;
        var shortcut = _config.Shortcuts[index];

        try
        {
            var info = new ProcessStartInfo(shortcut.FileName) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(shortcut.Arguments)) info.Arguments = shortcut.Arguments;

            Process.Start(info);
        }
        catch (Exception ex)
        {
            _notifier?.Show($"{shortcut.Label} didn't open", ex.Message);
        }
    }

    private void DeleteShortcut(int index)
    {
        if (index < 0 || index >= _config.Shortcuts.Count) return;

        var answer = MessageBox.Show(
            $"Remove the \"{_config.Shortcuts[index].Label}\" shortcut?",
            "Remove shortcut", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        _config.Shortcuts.RemoveAt(index);
        _config.Save();
        PushState();
    }

    private void DeletePreset(int index)
    {
        if (index < 0 || index >= _config.Presets.Count) return;
        var preset = _config.Presets[index];

        // MessageBox rather than an in-deck confirm: a dialog inside a non-activating window
        // can't reliably take the keyboard, and deleting a preset should be deliberate.
        var answer = MessageBox.Show(
            $"Delete the preset \"{preset.Name}\"?\n\nThis only removes the button. Nothing on your screen changes.",
            "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        _config.Presets.RemoveAt(index);
        _config.Save();
        PushState();
    }

    private void PushPreset(int index, string state, string? summary)
    {
        if (Web.CoreWebView2 is null) return;

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "preset",
            index,
            state,
            summary
        }));
    }

    /// <summary>
    /// Live threshold updates while the handle is being dragged: applied at once so the meter's
    /// over/under colouring tracks the mouse, but only written to disk on threshold-commit.
    /// </summary>
    private void SetThreshold(string raw)
    {
        if (_room is null) return;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return;

        value = Math.Clamp(value, 0, 100);
        _room.Threshold = value;
        _config.RoomThreshold = value;
    }

    private void PushState()
    {
        if (Web.CoreWebView2 is null || _mic is null) return;

        var byId = _mic.Devices.ToDictionary(d => d.Id);

        // Hardware names ("Focusrite USB Audio", "C922 Pro Stream Webcam") rather than endpoint
        // names ("Analogue 1 + 2", "Mikrofon") — on a narrow tile the hardware is what tells you
        // which physical thing is involved.
        string[] muteNames = _config.MuteDeviceIds
            .Select(id => byId.TryGetValue(id, out var d) ? d.Hardware : "(missing device)")
            .ToArray();

        string? roomName = _config.RoomSensorDeviceId is { } roomId && byId.TryGetValue(roomId, out var room)
            ? room.Hardware
            : null;

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "state",
            muted = !_mic.AnyLive,
            muteDevices = muteNames,
            roomDevice = roomName,
            roomArmed = _room?.Armed ?? false,
            roomRunning = _room?.IsRunning ?? false,
            roomThreshold = Math.Round(_config.RoomThreshold),
            roomError = _roomError,
            presets = _config.Presets.Select(p => new { name = p.Name, count = p.Entries.Count }).ToArray(),
            shortcuts = _config.Shortcuts.Select(s => new { label = s.Label, note = s.Note ?? "" }).ToArray()
        }));
    }

    private CapabilityUse _camera = CapabilityUse.None;
    private CapabilityUse _microphone = CapabilityUse.None;

    private void PollPrivacy()
    {
        var camera = CapabilityWatcher.Query("webcam");
        var microphone = CapabilityWatcher.Query("microphone");

        // Only push on change: this runs every second and the page repaints on every message.
        if (Same(camera, _camera) && Same(microphone, _microphone)) return;

        _camera = camera;
        _microphone = microphone;
        PushPrivacy();
    }

    private static bool Same(CapabilityUse a, CapabilityUse b) =>
        a.InUse == b.InUse && a.Describe() == b.Describe();

    private void PushPrivacy()
    {
        if (Web.CoreWebView2 is null) return;

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "privacy",
            camera = new { inUse = _camera.InUse, apps = _camera.Describe() },
            microphone = new { inUse = _microphone.InUse, apps = _microphone.Describe() }
        }));
    }

    private string _lastClockPayload = "";

    private void PushClock()
    {
        if (Web.CoreWebView2 is null) return;

        string payload = JsonSerializer.Serialize(new
        {
            type = "clock",
            cities = WorldClock.Now().Select(c => new
            {
                label = c.Label,
                time = c.Time,
                day = c.DayOffset,
                local = c.IsLocal
            })
        });

        // Ticks every second but the display only changes once a minute.
        if (payload == _lastClockPayload) return;

        _lastClockPayload = payload;
        Web.CoreWebView2.PostWebMessageAsJson(payload);
    }

    private void PushTimers()
    {
        if (Web.CoreWebView2 is null || _pomodoro is null || _stopwatch is null) return;

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "timers",
            pomodoro = new
            {
                phase = _pomodoro.Phase.ToString().ToLowerInvariant(),
                remaining = FormatCountdown(_pomodoro.Remaining),
                blocks = _pomodoro.CompletedBlocks,
                awaiting = _pomodoro.AwaitingNextBlock
            },
            stopwatch = new
            {
                running = _stopwatch.IsRunning,
                elapsed = FormatClock(_stopwatch.Elapsed),
                hasElapsed = _stopwatch.HasElapsed
            }
        }));
    }

    /// <summary>Rounded up, so a block that has just started reads 25:00 rather than 24:59.</summary>
    private static string FormatCountdown(TimeSpan remaining) =>
        FormatClock(TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)));

    private static string FormatClock(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{value.Minutes:00}:{value.Seconds:00}";

    private void PushLevel(double level)
    {
        if (Web.CoreWebView2 is null) return;

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "level",
            value = Math.Round(level, 1),
            over = level > (_room?.Threshold ?? 100)
        }));
    }

    private void SampleStats()
    {
        _system?.Sample();
        _gpu?.Sample();
        PushSystem();
    }

    private void PushSystem()
    {
        if (Web.CoreWebView2 is null || _system is null) return;

        bool hasGpu = _gpu is { IsAvailable: true };

        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "system",
            cpu = Math.Round(_system.CpuPercent),
            ram = Math.Round(_system.RamPercent),
            gpuAvailable = hasGpu,
            gpu = hasGpu ? Math.Round(_gpu!.GpuPercent) : 0
        }));
    }

    private void Cleanup()
    {
        App.ReleaseScreenSpace = null;
        _tickTimer?.Stop();
        _weatherTimer?.Stop();
        _weather?.Dispose();
        _claudeTimer?.Stop();
        _mediaTimer?.Stop();
        _mixer?.Dispose();
        _rearmTimer?.Stop();

        // The room monitor holds a capture stream on a device the mic controller owns —
        // it has to let go first.
        _room?.Dispose();
        _mic?.Dispose();

        _gpu?.Dispose();
        _hotkeys?.Dispose();
        _notifier?.Dispose();
        _tracker?.Dispose();
        _appBar?.Remove();
    }
}
