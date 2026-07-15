using System.Drawing;
using EveCommandCenter.Application.Diagnostics;
using Forms = System.Windows.Forms;

namespace EveCommandCenter.App.Shell;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.ContextMenuStrip contextMenu;
    private readonly Forms.NotifyIcon notifyIcon;
    private bool disposed;

    public TrayIconService(
        Action showSettings,
        Action togglePreviews,
        Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(showSettings);
        ArgumentNullException.ThrowIfNull(togglePreviews);
        ArgumentNullException.ThrowIfNull(exitApplication);

        contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Opening += (_, _) => AppLog.Information("Tray", "Tray menu opened.");
        contextMenu.Closed += (_, _) => AppLog.Debug("Tray", "Tray menu closed.");

        var settingsItem = new Forms.ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => ExecuteCommand("Settings", showSettings);

        var togglePreviewsItem = new Forms.ToolStripMenuItem("Show / hide previews");
        togglePreviewsItem.Click += (_, _) => ExecuteCommand("Show / hide previews", togglePreviews);

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExecuteCommand("Exit", exitApplication);

        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(togglePreviewsItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        notifyIcon = new Forms.NotifyIcon
        {
            Text = "EVE Command Center",
            Icon = SystemIcons.Application,
            ContextMenuStrip = contextMenu,
            Visible = true,
        };

        notifyIcon.DoubleClick += (_, _) => ExecuteCommand("Settings (double-click)", showSettings);
        AppLog.Information("Tray", "System tray icon created.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        AppLog.Information("Tray", "Disposing system tray icon.");
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        contextMenu.Dispose();
    }

    private static void ExecuteCommand(string commandName, Action action)
    {
        AppLog.SetCurrentOperation($"tray command: {commandName}");
        AppLog.Information("Tray", $"Tray command selected: {commandName}.");

        try
        {
            action();
            AppLog.Information("Tray", $"Tray command completed: {commandName}.");
        }
        catch (Exception exception)
        {
            AppLog.Error("Tray", $"Tray command failed: {commandName}.", exception);
            throw;
        }
        finally
        {
            AppLog.SetCurrentOperation("idle");
        }
    }
}
