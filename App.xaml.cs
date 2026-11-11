using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace HyperMouse;

public partial class App : Application
{
    private const string MutexName = "HyperMouse.SingleInstance";
    private const string ShowEventName = "HyperMouse.ShowWindow";

    private static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance: a second launch just brings the running window to front.
        _instanceMutex = new Mutex(true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* first instance still starting */ }
            Shutdown();
            return;
        }

        TryLog($"App.OnStartup at {DateTime.Now}");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);

        bool startInTray = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow();
        MainWindow = window;
        if (!startInTray) window.Show();
        else window.StartHiddenInTray();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray());
        }) { IsBackground = true, Name = "HyperMouse.ShowListener" };
        listener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TryLog($"App.OnExit with code {e.ApplicationExitCode} at {DateTime.Now}");
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void TryLog(string line)
    {
        try { File.AppendAllText(LogPath, line + "\n"); } catch { }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLog($"[CRASH Dispatcher] {e.Exception}");
        MessageBox.Show($"Hata:\n{e.Exception.Message}", "HyperMouse", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        TryLog($"[CRASH Domain] {e.ExceptionObject}");
    }
}
