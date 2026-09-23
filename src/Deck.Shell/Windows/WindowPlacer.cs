using Deck.Shell.Interop;
using Deck.Shell.Presets;
using static Deck.Shell.Interop.WindowNative;

namespace Deck.Shell.Windows;

internal static class WindowPlacer
{
    /// <summary>
    /// Moves a window to where a preset says it belongs. Returns false when Windows refuses,
    /// which in practice means the target runs elevated and this process does not.
    /// </summary>
    public static bool Place(IntPtr hwnd, PresetEntry entry)
    {
        if (!IsWindow(hwnd)) return false;

        // SetWindowPos is ignored on a maximised or minimised window, so it has to come down
        // to a normal state first.
        ShowWindow(hwnd, SW_RESTORE);

        bool moved = NativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero,
            entry.Left, entry.Top,
            Math.Max(entry.Width, 200), Math.Max(entry.Height, 150),
            SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        // Maximise applies to whichever monitor the window currently sits on — so the move
        // above is what chooses the screen. Order matters here.
        if (entry.IsMaximized) ShowWindow(hwnd, SW_MAXIMIZE);

        return moved;
    }
}
