using System.Windows;
using EveCommandCenter.Application.Discovery;
using EveCommandCenter.Presentation;
using EveCommandCenter.Windows.Discovery;

namespace EveCommandCenter.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var source = new Win32WindowSnapshotSource();
        var classifier = new EveWindowClassifier();
        var viewModel = new MainWindowViewModel(source, classifier);
        var mainWindow = new MainWindow(viewModel);

        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
