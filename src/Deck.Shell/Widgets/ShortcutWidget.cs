using System.Diagnostics;
using Deck.Shell.Config;

namespace Deck.Shell.Widgets;

internal sealed class ShortcutWidget(WidgetContext context, string id) : WidgetBase(context, "shortcut", id)
{
    private DeckShortcut? Shortcut => Context.Config.Shortcuts.FirstOrDefault(s => s.Id == Ref);

    public override bool Handle(string message)
    {
        if (message != "press") return false;

        Run();
        return true;
    }

    public override void Push()
    {
        if (Shortcut is not { } shortcut) return;

        Post(new { label = shortcut.Label, note = shortcut.Note ?? "" });
    }

    private void Run()
    {
        if (Shortcut is not { } shortcut) return;

        try
        {
            var info = new ProcessStartInfo(shortcut.FileName) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(shortcut.Arguments)) info.Arguments = shortcut.Arguments;

            Process.Start(info);
        }
        catch (Exception ex)
        {
            Context.Notifier.Show($"{shortcut.Label} didn't open", ex.Message);
        }
    }
}
