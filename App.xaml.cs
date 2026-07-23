using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FastExplorer;

public partial class App : Application
{
    private Window? _window;
    private static DispatcherQueue? _dispatcherQueue;

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastExplorer", "crash.log");

    public App()
    {
        InitializeComponent();

        // This app previously had no top-level exception handler at all - several
        // features' code comments (RecycleBinView, DiskUsageView, ImageBatchView) note
        // that an exception escaping a XAML-invoked event handler with nothing to catch
        // it takes the *entire process* down, often as an uncatchable native fail-fast
        // (0xc000027b/STOWED_EXCEPTION) rather than a normal .NET exception - a plain
        // try/catch around the offending call doesn't help once it's already gotten that
        // far. Subscribing here is what actually intercepts it before that happens:
        // WinUI raises this event for exceptions unhandled anywhere in a XAML callback,
        // and setting e.Handled = true tells it to suppress the crash instead. Logging
        // first, since without this we've had to reconstruct what went wrong from
        // Windows' own crash reporting after the fact.
        UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath)!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging the crash must never itself become the reason the app still goes
            // down - if it can't write the log, there's nothing more to try here.
        }

        e.Handled = true;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _dispatcherQueue = _window.DispatcherQueue;
        _window.Activate();
    }

    public static void EnqueueOnUiThread(Action action)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }
}
