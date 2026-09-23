namespace Deck.Shell.Widgets;

/// <summary>
/// Something several widgets lean on — a timer, a poll. It runs while at least one widget on the
/// deck is using it and stops when the last one leaves, so "in the library" really means off.
/// </summary>
internal abstract class SharedService
{
    private int _users;

    public bool IsRunning => _users > 0;

    public void Acquire()
    {
        if (_users++ == 0) OnStart();
    }

    public void Release()
    {
        if (_users == 0) return;
        if (--_users == 0) OnStop();
    }

    protected abstract void OnStart();

    protected abstract void OnStop();
}
