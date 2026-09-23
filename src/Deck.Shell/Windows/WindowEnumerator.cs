using System.IO;
using Deck.Shell.Interop;
using static Deck.Shell.Interop.WindowNative;

namespace Deck.Shell.Windows;

internal sealed record LiveWindow(
    IntPtr Handle,
    string Title,
    string ProcessName,
    string ExecutablePath,
    NativeMethods.RECT Bounds,
    bool IsMaximized,
    string MonitorDevice);

internal static class WindowEnumerator
{
    /// <summary>Shell-owned windows that are never part of a user's workspace.</summary>
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",           // desktop
        "WorkerW",           // desktop wallpaper host
        "Shell_TrayWnd",     // taskbar
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
    };

    /// <summary>
    /// Top-level windows a person would recognise as "an app on my screen". Every filter here
    /// exists because without it the capture list fills with things the user never opened.
    /// </summary>
    public static List<LiveWindow> VisibleTopLevel()
    {
        var results = new List<LiveWindow>();
        uint ownPid = (uint)Environment.ProcessId;
        var monitors = Monitors.All();

        EnumWindowsProc callback = (hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;   // dialogs and popups

            long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

            string title = NativeMethods.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title) || title == "(untitled)") return true;

            if (IgnoredClasses.Contains(GetClassName(hWnd))) return true;
            if (IsCloaked(hWnd)) return true;

            _ = NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == ownPid) return true;

            if (!GetWindowRect(hWnd, out var bounds)) return true;
            if (bounds.Width <= 0 || bounds.Height <= 0) return true;

            string path = GetProcessPath(pid);

            var placement = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
            bool maximized = GetWindowPlacement(hWnd, ref placement) && placement.showCmd == SW_SHOWMAXIMIZED;

            results.Add(new LiveWindow(
                hWnd,
                title,
                string.IsNullOrEmpty(path) ? "(unknown)" : Path.GetFileNameWithoutExtension(path),
                path,
                bounds,
                maximized,
                MonitorNameFor(bounds, monitors)));

            return true;
        };

        EnumWindows(callback, IntPtr.Zero);
        return results;
    }

    /// <summary>The monitor a window mostly sits on, by centre point.</summary>
    private static string MonitorNameFor(NativeMethods.RECT bounds, List<MonitorSpec> monitors)
    {
        int cx = bounds.left + bounds.Width / 2;
        int cy = bounds.top + bounds.Height / 2;

        foreach (var m in monitors)
        {
            if (cx >= m.Bounds.left && cx < m.Bounds.right && cy >= m.Bounds.top && cy < m.Bounds.bottom)
                return m.Device;
        }

        return monitors.FirstOrDefault(m => m.IsPrimary)?.Device ?? "(unknown)";
    }
}
