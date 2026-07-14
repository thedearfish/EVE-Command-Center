using System.ComponentModel;
using System.Windows;
using EveCommandCenter.App.Startup;
using EveCommandCenter.Application.Diagnostics;
using EveCommandCenter.Presentation;

namespace EveCommandCenter.App.Shell;

public sealed class ApplicationLifecycleController : IDisposable
{
    private readonly MainWindow settingsWindow;
    private readonly FirstRunStateStore firstRunStateStore;
    private readonly Action<int> shutdown;
    private readonly TrayIconService trayIcon;
    private bool firstRunPending;
    private bool exitRequested;
    private bool windowClosed;
    private bool disposed;

    public ApplicationLifecycleController(
        MainWindow settingsWindow,
        FirstRunStateStore firstRunStateStore,
        Action togglePreviews,
        Action<int> shutdown)
    {
        this.settingsWindow = settingsWindow;
        this.firstRunStateStore = firstRunStateStore;
        this.shutdown = shutdown;

        settingsWindow.Closing += OnSettingsWindowClosing;
        settingsWindow.Closed += OnSettingsWindowClosed;
        trayIcon = new TrayIconService(ShowSettings, togglePreviews, () => RequestExit(0));
    }

    public void Start(bool forceSettingsWindow)
    {
        firstRunPending = firstRunStateStore.IsFirstRun();
        AppLog.Information(
            "Lifecycle",
            $"Lifecycle started; firstRun={firstRunPending}; forceSettings={forceSettingsWindow}.");

        if (!firstRunPending && !forceSettingsWindow)
        {
            AppLog.Information("Lifecycle", "Settings window remains hidden on startup.");
            return;
        }

        ShowSettings();
    }

    public void ShowSettings()
    {
        if (windowClosed || exitRequested)
        {
            AppLog.Warning(
                "Lifecycle",
                $"Settings window show request ignored; windowClosed={windowClosed}; exitRequested={exitRequested}.");
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
        AppLog.Information("Lifecycle", "Settings window shown and activated.");
    }

    public void RequestExit(int exitCode)
    {
        if (exitRequested)
        {
            return;
        }

        exitRequested = true;
        AppLog.Information("Lifecycle", $"Application exit requested with code {exitCode}.");
        CompleteFirstRunIfNeeded();
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
        AppLog.Information("Lifecycle", "Application lifecycle controller disposed.");
    }

    private void OnSettingsWindowClosing(object? sender, CancelEventArgs e)
    {
        if (exitRequested)
        {
            return;
        }

        CompleteFirstRunIfNeeded();
        e.Cancel = true;
        settingsWindow.Hide();
        AppLog.Information("Lifecycle", "Settings window close intercepted and hidden to tray.");
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        windowClosed = true;
        AppLog.Information("Lifecycle", "Settings window closed.");
    }

    private void CompleteFirstRunIfNeeded()
    {
        if (!firstRunPending)
        {
            return;
        }

        try
        {
            firstRunStateStore.MarkCompleted();
            firstRunPending = false;
            AppLog.Information("Lifecycle", "First-run setup marked as completed.");
        }
        catch (Exception exception)
        {
            AppLog.Warning(
                "Lifecycle",
                "Could not persist first-run completion state; setup may appear again.",
                exception);
        }
    }
}
