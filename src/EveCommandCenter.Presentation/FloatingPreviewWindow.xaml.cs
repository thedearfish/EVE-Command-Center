using System.Windows;
using System.Windows.Input;

namespace EveCommandCenter.Presentation;

public partial class FloatingPreviewWindow : Window
{
    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.Register(
        nameof(ShowHeader),
        typeof(bool),
        typeof(FloatingPreviewWindow),
        new PropertyMetadata(true));

    private readonly DetectedClientViewModel client;
    private readonly Func<long, Task> activateClient;

    public FloatingPreviewWindow(
        DetectedClientViewModel client,
        Func<long, Task> activateClient)
    {
        this.client = client;
        this.activateClient = activateClient;

        InitializeComponent();
        DataContext = client;
    }

    public bool ShowHeader
    {
        get => (bool)GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    private async void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            await activateClient(client.SourceWindowId);
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            await activateClient(client.SourceWindowId);
        }
    }
}
