using static Deck.Shell.Interop.NativeMethods;

namespace Deck.Shell.Interop;

/// <summary>
/// Remembers the last foreground window that wasn't ours.
///
/// The deck is built never to take focus, but "never" is a claim about Win32 behaviour we don't
/// fully control — WebView2 in particular is known to call SetFocus on click. This tracker is the
/// safety net: whatever the deck does to focus, we still know which window the user was actually
/// working in, so an action can target it explicitly instead of trusting GetForegroundWindow at
/// the moment of the click.
/// </summary>
internal sealed class ForegroundTracker : IDisposable
{
    private readonly uint _ownProcessId;
    private readonly WinEventProc _proc;   // must be held: the hook stores a raw function pointer
    private IntPtr _hook;

    public IntPtr LastForeground { get; private set; }
    public string LastForegroundTitle => GetWindowTitle(LastForeground);

    public ForegroundTracker()
    {
        _ownProcessId = (uint)Environment.ProcessId;
        _proc = OnForegroundChanged;

        _hook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        // Seed with whatever is in front right now, so the very first press has a target.
        var current = GetForegroundWindow();
        if (!IsOwnWindow(current)) LastForeground = current;
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        // idObject != OBJID_WINDOW (0) fires for menus, carets and the like — ignore those.
        if (idObject != 0 || hwnd == IntPtr.Zero) return;
        if (IsOwnWindow(hwnd)) return;
        LastForeground = hwnd;
    }

    private bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == _ownProcessId;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
