using static Deck.Shell.Interop.HotkeyNative;

namespace Deck.Shell.Hotkeys;

/// <summary>
/// Owns the process's global hotkey registrations. Registration is per-window, and the deck's
/// window works fine for this despite never taking focus — that's the point.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly Dictionary<int, string> _idToAction = [];
    private int _nextId = 1;

    /// <summary>Combos Windows refused, usually because another app already owns them.</summary>
    public List<string> Conflicts { get; } = [];

    public event Action<string>? Triggered;

    public HotkeyManager(IntPtr hwnd) => _hwnd = hwnd;

    public void Apply(IEnumerable<HotkeyBinding> bindings)
    {
        UnregisterAll();

        foreach (var binding in bindings)
        {
            if (!binding.IsSet) continue;

            int id = _nextId++;
            if (RegisterHotKey(_hwnd, id, binding.Modifiers | MOD_NOREPEAT, binding.VirtualKey))
            {
                _idToAction[id] = binding.Action;
            }
            else
            {
                // Silent failure here would be the worst outcome: the user presses their key
                // forever and quietly concludes the deck is broken.
                Conflicts.Add(binding.Display);
            }
        }
    }

    /// <summary>Returns true when the message was a hotkey we own.</summary>
    public bool HandleMessage(int msg, IntPtr wParam)
    {
        if (msg != WM_HOTKEY) return false;
        if (_idToAction.TryGetValue(wParam.ToInt32(), out string? action)) Triggered?.Invoke(action);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _idToAction.Keys) UnregisterHotKey(_hwnd, id);
        _idToAction.Clear();
        Conflicts.Clear();
    }

    public void Dispose() => UnregisterAll();
}
