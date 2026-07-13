using System.ComponentModel;
using System.Windows;
using EveCommandCenter.App.Startup;
using EveCommandCenter.Presentation;

namespace EveCommandCenter.App.Shell;

public sealed class ApplicationLifecycleController : IDisposable
{
    private readonly MainWindow settingsWindow;
    private readonly FirstRunStateStore firstRunStateStore;
    private readonly Action<int> shutdown;
    private readonly TrayIconService trayIcon;
    private bool exitRequested;
    private bool windowClosed;
    private bool disposed;

    public ApplicationLifecycleController(
        MainWindow settingsWindow,
        FirstRunStateStore firstRunStateStore,
        Action<int> shutdown)
    {
        this.settingsWindow = settingsWindow;
        this.firstRunStateStore = firstRunStateStore;
        this.shutdown = shutdown;

        settingsWindow.Closing += OnSettingsWindowClosing;
        settingsWindow.Closed += OnSettingsWindowClosed;
        trayIcon = new TrayIconService(ShowSettings, () => RequestExit(0));
    }

    public void Start(bool forceSettingsWindow)
    {
        bool firstRun = firstRunStateStore.IsFirstRun();
        if (!firstRun && !forceSettingsWindow)
        {
            return;
        }

        ShowSettings();

        if (firstRun)
        {
            try
            {
                firstRunStateStore.MarkCompleted();
            }
            catch
            {
                // Keep running. A write failure only means setup may be shown again next launch.
            }
        }
    }

    public void ShowSettings()
    {
        if (windowClosed || exitRequested)
        {
            return;
        }

        if (!settingsWindow.IsVisible)
        {
            settingsWindow.Show();
        }

        if (settingsWindow.WindowState == WindowState.Minimized)
        {
            settingsWindow.WindowState = WindowState.Normal;
        }

        settingsWindow.Activate();
        settingsWindow.Focus();
    }

    public void RequestExit(int exitCode)
    {
        if (exitRequested)
        {
            return;
        }

        exitRequested = true;
        trayIcon.Dispose();

        if (!windowClosed)
        {
            settingsWindow.Close();
        }

        shutdown(exitCode);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        settingsWindow.Closing -= OnSettingsWindowClosing;
        settingsWindow.Closed -= OnSettingsWindowClosed;
        trayIcon.Dispose();
    }

    private void OnSettingsWindowClosing(object? sender, CancelEventArgs e)
    {
        if (exitRequested)
        {
            return;
        }

        e.Cancel = true;
        settingsWindow.Hide();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        windowClosed = true;
    }
}
