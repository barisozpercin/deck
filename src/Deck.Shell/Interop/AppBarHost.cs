using System.Runtime.InteropServices;
using static Deck.Shell.Interop.NativeMethods;

namespace Deck.Shell.Interop;

/// <summary>
/// Registers the deck as a Windows AppBar so it reserves screen space the way the taskbar does —
/// maximised windows stop at its edge instead of going underneath it.
///
/// The reservation is process-global state owned by the shell, NOT by our window. If we exit
/// without sending ABM_REMOVE the space stays reserved until explorer restarts, and the user is
/// left with a dead strip on their monitor. Every exit path must reach <see cref="Remove"/>.
/// </summary>
internal sealed class AppBarHost : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly uint _callbackMessage;
    private bool _registered;

    public uint CallbackMessage => _callbackMessage;
    public RECT CurrentRect { get; private set; }

    public AppBarHost(IntPtr hwnd)
    {
        _hwnd = hwnd;
        // A unique message name so our callback can't collide with another appbar's.
        _callbackMessage = RegisterWindowMessage("DeckAppBarMessage_2A7F1C");
    }

    public bool Register()
    {
        if (_registered) return true;

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = _hwnd,
            uCallbackMessage = _callbackMessage
        };

        var result = SHAppBarMessage(ABM_NEW, ref abd);
        _registered = result != UIntPtr.Zero;
        return _registered;
    }

    /// <summary>
    /// Docks to the bottom edge of <paramref name="monitor"/>, claiming <paramref name="heightPx"/>
    /// physical pixels. Coordinates are virtual-desktop pixels and may legitimately be negative —
    /// this desktop has monitors at x = -1080 and y = -339.
    /// </summary>
    public RECT Dock(MonitorSpec monitor, int heightPx)
    {
        if (!_registered) throw new InvalidOperationException("Register() first.");

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = _hwnd,
            uEdge = ABE_BOTTOM,
            rc = new RECT
            {
                left = monitor.Bounds.left,
                right = monitor.Bounds.right,
                top = monitor.Bounds.bottom - heightPx,
                bottom = monitor.Bounds.bottom
            }
        };

        // QUERYPOS only shoves the rect clear of other appbars; it does not preserve our
        // thickness, so the edge dimension has to be re-derived afterwards.
        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        abd.rc.top = abd.rc.bottom - heightPx;

        SHAppBarMessage(ABM_SETPOS, ref abd);
        CurrentRect = abd.rc;

        SetWindowPos(_hwnd, HWND_TOPMOST,
            abd.rc.left, abd.rc.top, abd.rc.Width, abd.rc.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        return abd.rc;
    }

    /// <summary>Re-applies our window position after the shell tells us the layout moved.</summary>
    public void ReapplyPosition()
    {
        if (!_registered) return;
        var r = CurrentRect;
        SetWindowPos(_hwnd, HWND_TOPMOST, r.left, r.top, r.Width, r.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void Remove()
    {
        if (!_registered) return;

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = _hwnd
        };
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;
    }

    public void Dispose() => Remove();
}
