using System.Runtime.InteropServices;
using static Deck.Shell.Interop.WindowNative;

namespace Deck.Shell.Interop;

/// <summary>
/// Brings another application's window to the front.
///
/// This is the one place the deck deliberately touches foreground state — everywhere else the
/// rule is never to. Windows also refuses <c>SetForegroundWindow</c> from a process that isn't
/// already foreground, which the deck never is, so the call has to borrow the current
/// foreground thread's input queue to be allowed through.
/// </summary>
internal static class WindowFocus
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Focuses a window belonging to <paramref name="pid"/>, or to one of its ancestors.
    /// Claude Code's session process owns no window — the window belongs to its parent.
    /// </summary>
    public static bool FocusProcessWindow(int pid)
    {
        for (int depth = 0; depth < 4 && pid > 0; depth++)
        {
            IntPtr hwnd = FindTopLevelWindow(pid);
            if (hwnd != IntPtr.Zero) return Focus(hwnd);

            pid = ProcessTree.GetParent(pid);
        }

        return false;
    }

    private static IntPtr FindTopLevelWindow(int pid)
    {
        IntPtr found = IntPtr.Zero;

        EnumWindowsProc callback = (hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;
            if (IsCloaked(hWnd)) return true;
            if (string.IsNullOrWhiteSpace(NativeMethods.GetWindowTitle(hWnd))) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint owner);
            if (owner != (uint)pid) return true;

            found = hWnd;
            return false;
        };

        EnumWindows(callback, IntPtr.Zero);
        return found;
    }

    private static bool Focus(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        uint ownThread = GetCurrentThreadId();

        bool attachedForeground = foregroundThread != 0 && foregroundThread != ownThread
            && AttachThreadInput(ownThread, foregroundThread, true);
        bool attachedTarget = targetThread != 0 && targetThread != ownThread
            && AttachThreadInput(ownThread, targetThread, true);

        try
        {
            BringWindowToTop(hwnd);
            return SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget) AttachThreadInput(ownThread, targetThread, false);
            if (attachedForeground) AttachThreadInput(ownThread, foregroundThread, false);
        }
    }
}
