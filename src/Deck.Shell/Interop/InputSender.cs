using System.Runtime.InteropServices;
using static Deck.Shell.Interop.NativeMethods;

namespace Deck.Shell.Interop;

internal static class InputSender
{
    /// <summary>
    /// Types a literal string into whatever currently has keyboard focus, using scan-code
    /// Unicode events so it doesn't depend on the active keyboard layout.
    /// Returns the number of input events Windows accepted (0 means it was blocked — most
    /// commonly UIPI refusing to let a non-elevated process send input to an elevated window).
    /// </summary>
    public static uint SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(MakeUnicodeKey(c, up: false));
            inputs.Add(MakeUnicodeKey(c, up: true));
        }

        var array = inputs.ToArray();
        return SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeUnicodeKey(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };
}
