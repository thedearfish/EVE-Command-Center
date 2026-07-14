using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace EveCommandCenter.Presentation;

public sealed class PreviewWindowLayoutChangedEventArgs(
    double left,
    double top,
    double width,
    double height) : EventArgs
{
    public double Left { get; } = left;

    public double Top { get; } = top;

    public double Width { get; } = width;

    public double Height { get; } = height;
}

public partial class FloatingPreviewWindow : Window
{
    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.Register(
        nameof(ShowHeader),
        typeof(bool),
        typeof(FloatingPreviewWindow),
        new PropertyMetadata(true));

    public static readonly DependencyProperty CustomLabelProperty = DependencyProperty.Register(
        nameof(CustomLabel),
        typeof(string),
        typeof(FloatingPreviewWindow),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ContentModeProperty = DependencyProperty.Register(
        nameof(ContentMode),
        typeof(PreviewContentMode),
        typeof(FloatingPreviewWindow),
        new PropertyMetadata(PreviewContentMode.Standard));

    private readonly DetectedClientViewModel client;
    private readonly Func<long, Task> activateClient;
    private readonly DispatcherTimer layoutCommitTimer;
    private Point? dragStartScreen;
    private bool potentialClick;
    private bool layoutTrackingEnabled;
    private bool suppressLayoutTracking;

    public FloatingPreviewWindow(
        DetectedClientViewModel client,
        Func<long, Task> activateClient)
    {
        this.client = client;
        this.activateClient = activateClient;

        layoutCommitTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        layoutCommitTimer.Tick += OnLayoutCommitTimerTick;

        InitializeComponent();
        DataContext = client;

        LocationChanged += OnWindowLayoutChanged;
        SizeChanged += OnWindowSizeChanged;
        Closed += OnWindowClosed;
    }

    public event EventHandler<PreviewWindowLayoutChangedEventArgs>? LayoutCommitted;

    public bool ShowHeader
    {
        get => (bool)GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    public string CustomLabel
    {
        get => (string)GetValue(CustomLabelProperty);
        set => SetValue(CustomLabelProperty, value ?? string.Empty);
    }

    public PreviewContentMode ContentMode
    {
        get => (PreviewContentMode)GetValue(ContentModeProperty);
        set => SetValue(ContentModeProperty, value);
    }

    public void ApplyBounds(double left, double top, double width, double height)
    {
        suppressLayoutTracking = true;
        try
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
        finally
        {
            suppressLayoutTracking = false;
        }
    }

    public void ApplySize(double width, double height)
    {
        suppressLayoutTracking = true;
        try
        {
            Width = width;
            Height = height;
        }
        finally
        {
            suppressLayoutTracking = false;
        }
    }

    public void EnableLayoutTracking() => layoutTrackingEnabled = true;

    public PreviewWindowLayoutChangedEventArgs CaptureLayout()
    {
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;
        return new PreviewWindowLayoutChangedEventArgs(Left, Top, width, height);
    }

    private void OnSurfacePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        dragStartScreen = PointToScreen(e.GetPosition(this));
        potentialClick = true;
        _ = Mouse.Capture(this);
        e.Handled = true;
    }

    private void OnSurfacePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (dragStartScreen is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point currentScreen = PointToScreen(e.GetPosition(this));
        double horizontalDistance = Math.Abs(currentScreen.X - dragStartScreen.Value.X);
        double verticalDistance = Math.Abs(currentScreen.Y - dragStartScreen.Value.Y);

        if (horizontalDistance < SystemParameters.MinimumHorizontalDragDistance &&
            verticalDistance < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        potentialClick = false;
        dragStartScreen = null;
        Mouse.Capture(null);

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse button can be released between the movement check and DragMove.
        }

        e.Handled = true;
    }

    private async void OnSurfacePreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        bool shouldActivate = potentialClick;
        potentialClick = false;
        dragStartScreen = null;
        Mouse.Capture(null);
        e.Handled = true;

        if (shouldActivate)
        {
            await activateClient(client.SourceWindowId);
        }
    }

    private void OnWindowLayoutChanged(object? sender, EventArgs e) => ScheduleLayoutCommit();

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => ScheduleLayoutCommit();

    private void ScheduleLayoutCommit()
    {
        if (!layoutTrackingEnabled || suppressLayoutTracking || WindowState != WindowState.Normal)
        {
            return;
        }

        layoutCommitTimer.Stop();
        layoutCommitTimer.Start();
    }

    private void OnLayoutCommitTimerTick(object? sender, EventArgs e)
    {
        layoutCommitTimer.Stop();
        LayoutCommitted?.Invoke(this, CaptureLayout());
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        layoutCommitTimer.Stop();
        layoutCommitTimer.Tick -= OnLayoutCommitTimerTick;
        LocationChanged -= OnWindowLayoutChanged;
        SizeChanged -= OnWindowSizeChanged;
        Closed -= OnWindowClosed;
        Mouse.Capture(null);
    }
}
