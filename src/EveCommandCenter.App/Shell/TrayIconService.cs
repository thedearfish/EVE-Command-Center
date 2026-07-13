using System.Drawing;
using Forms = System.Windows.Forms;

namespace EveCommandCenter.App.Shell;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.ContextMenuStrip contextMenu;
    private readonly Forms.NotifyIcon notifyIcon;
    private bool disposed;

    public TrayIconService(Action showSettings, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(showSettings);
        ArgumentNullException.ThrowIfNull(exitApplication);

        contextMenu = new Forms.ContextMenuStrip();

        var settingsItem = new Forms.ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => showSettings();

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => exitApplication();

        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        notifyIcon = new Forms.NotifyIcon
        {
            Text = "EVE Command Center",
            Icon = SystemIcons.Application,
            ContextMenuStrip = contextMenu,
            Visible = true,
        };

        notifyIcon.DoubleClick += (_, _) => showSettings();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        contextMenu.Dispose();
    }
}
