using System.Drawing;
using WinForms = System.Windows.Forms;

namespace Deck.Shell.Notifications;

/// <summary>
/// Windows notifications via a tray icon. Chosen over toast APIs because those require a
/// registered AUMID and a Start Menu shortcut for unpackaged apps — this needs neither, and the
/// tray icon doubles as a way out if the deck's own EXIT tile is ever unreachable.
/// </summary>
internal sealed class Notifier : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public event Action? ExitRequested;

    private readonly WinForms.ContextMenuStrip _menu = new();
    private readonly Icon _trayIcon = DeckIcon.Create();

    public Notifier()
    {
        _icon = new WinForms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Deck",
            Visible = true,
            ContextMenuStrip = _menu
        };
    }

    public void AddItem(string text, Action onClick) =>
        _menu.Items.Add(text, null, (_, _) => onClick());

    /// <summary>A checkable tray item. Anything that runs at boot needs a visible off switch.</summary>
    public void AddToggle(string text, bool isChecked, Action<bool> onToggle)
    {
        var item = new WinForms.ToolStripMenuItem(text) { Checked = isChecked, CheckOnClick = true };
        item.CheckedChanged += (_, _) => onToggle(item.Checked);
        _menu.Items.Add(item);
    }

    public void AddExitItem()
    {
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Exit deck", null, (_, _) => ExitRequested?.Invoke());
    }

    public void Show(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = WinForms.ToolTipIcon.None;
        _icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _trayIcon.Dispose();
    }
}
