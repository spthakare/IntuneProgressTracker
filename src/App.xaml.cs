using System.Threading;
using System.Windows;

namespace IntuneProgressTracker;

public partial class App : Application
{
    private static Mutex? _mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Global\\EOG.IntuneProgressTracker.v4", out var created);
        if (!created) { Shutdown(); return; }

        var marker = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EOG", "IntuneProgressTracker", ".setup-complete");
        if (System.IO.File.Exists(marker))
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
}
