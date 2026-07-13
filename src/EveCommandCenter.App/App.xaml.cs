using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using EveCommandCenter.Application.Discovery;
using EveCommandCenter.Presentation;
using EveCommandCenter.Windows.Discovery;

namespace EveCommandCenter.App;

public partial class App : System.Windows.Application
{
    private static readonly object LogSync = new();
    private static readonly string LogPath = CreateLogPath();

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        WriteLog("Application object created.");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            WriteLog($"Startup begin. Base directory: {AppContext.BaseDirectory}");
            WriteLog($"OS: {Environment.OSVersion}; 64-bit process: {Environment.Is64BitProcess}");

            base.OnStartup(e);
            WriteLog("WPF base startup completed.");

            var source = new Win32WindowSnapshotSource();
            WriteLog("Window snapshot source created.");

            var classifier = new EveWindowClassifier();
            var viewModel = new MainWindowViewModel(source, classifier);
            WriteLog("Main window view model created.");

            var mainWindow = new MainWindow(viewModel);
            WriteLog("Main window created.");

            MainWindow = mainWindow;
            mainWindow.Show();
            WriteLog("Main window shown successfully.");
        }
        catch (Exception exception)
        {
            ReportFatalStartupError(exception);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WriteLog($"Application exit. Code: {e.ApplicationExitCode}.");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog("Unhandled dispatcher exception.", e.Exception);
        ShowError("EVE Command Center encountered an unexpected error.", e.Exception);
        e.Handled = true;
        Shutdown(-2);
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        WriteLog($"Unhandled AppDomain exception. Terminating: {e.IsTerminating}.", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteLog("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    private static void ReportFatalStartupError(Exception exception)
    {
        WriteLog("Fatal startup error.", exception);
        ShowError("EVE Command Center could not start.", exception);
    }

    private static void ShowError(string title, Exception exception)
    {
        string message = $"{exception.GetType().FullName}: {exception.Message}\n\n" +
                         $"Diagnostic log:\n{LogPath}";

        try
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Logging is the final fallback if WPF cannot display a dialog.
        }
    }

    private static string CreateLogPath()
    {
        string[] candidateDirectories =
        [
            Path.Combine(AppContext.BaseDirectory, "logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EVE Command Center", "logs"),
            Path.Combine(Path.GetTempPath(), "EVE Command Center", "logs"),
        ];

        foreach (string directory in candidateDirectories)
        {
            try
            {
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, "startup.log");
            }
            catch
            {
                // Try the next writable location.
            }
        }

        return Path.Combine(Path.GetTempPath(), "eve-command-center-startup.log");
    }

    private static void WriteLog(string message, Exception? exception = null)
    {
        try
        {
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [")
                .Append(Environment.ProcessId)
                .Append("] ")
                .AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (LogSync)
            {
                File.AppendAllText(LogPath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Startup diagnostics must never cause a secondary crash.
        }
    }
}
