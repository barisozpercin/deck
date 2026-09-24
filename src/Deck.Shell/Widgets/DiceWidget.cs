namespace Deck.Shell.Widgets;

/// <summary>
/// The roll happens in the page, so the tumble starts on the click with no round trip. The host
/// only keeps the mode (saved) and the last result (for this session), so a layout change
/// doesn't wipe the face.
/// </summary>
internal sealed class DiceWidget(WidgetContext context) : WidgetBase(context, "dice")
{
    public static readonly string[] Modes = ["d6", "d20", "coin"];

    private const string RolledPrefix = "rolled:";

    private string? _last;

    private string Mode => Modes.Contains(Context.Config.DiceMode) ? Context.Config.DiceMode : Modes[0];

    public override bool Handle(string message)
    {
        if (message == "mode")
        {
            Context.Config.DiceMode = Modes[(Array.IndexOf(Modes, Mode) + 1) % Modes.Length];
            Context.Config.Save();
            _last = null;
            Push();
            return true;
        }

        if (message.StartsWith(RolledPrefix, StringComparison.Ordinal))
        {
            string value = message[RolledPrefix.Length..];
            if (IsResult(Mode, value)) _last = value;
            // Echoed back so the page's cached copy matches what the face now shows.
            Push();
            return true;
        }

        return false;
    }

    public override void Push() => Post(new { mode = Mode, last = _last });

    /// <summary>Only a result the current mode could produce is remembered.</summary>
    public static bool IsResult(string mode, string value) => mode switch
    {
        "coin" => value is "heads" or "tails",
        "d20" => int.TryParse(value, out int d20) && d20 is >= 1 and <= 20,
        _ => int.TryParse(value, out int d6) && d6 is >= 1 and <= 6
    };
}
