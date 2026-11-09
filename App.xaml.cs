using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace HyperMouse;

public partial class App : Application
{
    private static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        File.AppendAllText(LogPath, $"App.OnStartup at {DateTime.Now}\n");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        File.AppendAllText(LogPath, $"App.OnExit with code {e.ApplicationExitCode} at {DateTime.Now}\n");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        File.AppendAllText(LogPath, $"[CRASH Dispatcher] {e.Exception}\n");
        MessageBox.Show($"Hata:\n{e.Exception.Message}", "HyperMouse Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        File.AppendAllText(LogPath, $"[CRASH Domain] {e.ExceptionObject}\n");
    }
}
