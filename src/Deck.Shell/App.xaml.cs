using System.Windows;

namespace Deck.Shell;

public partial class App : Application
{
    /// <summary>
    /// Set by MainWindow so that any exit path — clean shutdown, unhandled exception, or the
    /// process being torn down — still releases the AppBar's reserved screen space. Leaking it
    /// leaves a dead strip on the user's monitor until explorer restarts.
    /// </summary>
    internal static Action? ReleaseScreenSpace;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Release();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Release();
        DispatcherUnhandledException += (_, args) =>
        {
            Release();
            MessageBox.Show(args.Exception.ToString(), "Deck crashed");
            args.Handled = false;
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Release();
        base.OnExit(e);
    }

    private static void Release()
    {
        var release = ReleaseScreenSpace;
        ReleaseScreenSpace = null;   // idempotent: several exit paths can fire
        release?.Invoke();
    }
}
