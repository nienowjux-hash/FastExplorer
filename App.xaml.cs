using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FastExplorer;

public partial class App : Application
{
    private Window? _window;
    private static DispatcherQueue? _dispatcherQueue;

    public App()
    {
        InitializeComponent();
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
