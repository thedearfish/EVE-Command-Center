using System.Windows;
using EveCommandCenter.Presentation;

namespace EveCommandCenter.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
