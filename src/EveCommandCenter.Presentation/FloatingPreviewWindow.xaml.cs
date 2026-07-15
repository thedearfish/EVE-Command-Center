using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using EveCommandCenter.Application.Diagnostics;

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
    private const double ResizeBorderDip = 8;

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

    public static readonly DependencyProperty IsClientActiveProperty = DependencyProperty.Register(
        nameof(IsClientActive),
        typeof(bool),
        typeof(FloatingPreviewWindow),
        new PropertyMetadata(false));

    private readonly DetectedClientViewModel client;
    private readonly Func<long, Task> activateClient;
    private bool layoutTrackingEnabled;
    private bool suppressLayoutTracking;

    private Point? pointerStartScreen;
    private Rect pointerStartBounds;
    private double pointerStartDpiScaleX = 1.0;
    private double pointerStartDpiScaleY = 1.0;
    private bool pointerInteractionActive;
    private bool manualMoveStarted;
    private bool manualResizeStarted;
    private ResizeEdges pointerResizeEdges;

    public FloatingPreviewWindow(
        DetectedClientViewModel client,
        Func<long, Task> activateClient)
    {
        this.client = client;
        this.activateClient = activateClient;

        InitializeComponent();
        DataContext = client;

        SourceInitialized += OnSourceInitialized;
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

    public bool IsClientActive
    {
        get => (bool)GetValue(IsClientActiveProperty);
        set => SetValue(IsClientActiveProperty, value);
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
        Rect bounds = CaptureBounds();
        return new PreviewWindowLayoutChangedEventArgs(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        AppLog.Information(
            "PreviewWindow",
            $"Native preview window initialized for {client.DisplayName}; HWND=0x{handle.ToInt64():X}.");
    }

    private void OnSurfacePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            e.LeftButton != MouseButtonState.Pressed ||
            pointerInteractionActive)
        {
            return;
        }

        Point localPoint = e.GetPosition(this);
        pointerResizeEdges = GetResizeEdges(localPoint);
        pointerInteractionActive = true;
        manualMoveStarted = false;
        manualResizeStarted = false;
        pointerStartScreen = PointToScreen(localPoint);
        pointerStartBounds = CaptureBounds();

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        pointerStartDpiScaleX = Math.Max(0.1, dpi.DpiScaleX);
        pointerStartDpiScaleY = Math.Max(0.1, dpi.DpiScaleY);

        Cursor = GetResizeCursor(pointerResizeEdges);
        _ = Mouse.Capture(this, CaptureMode.SubTree);
        e.Handled = true;
    }

    private void OnSurfacePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!pointerInteractionActive || pointerStartScreen is null)
        {
            Cursor = GetResizeCursor(GetResizeEdges(e.GetPosition(this)));
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishPointerInteraction(commitLayout: manualMoveStarted || manualResizeStarted);
            return;
        }

        Point currentScreen = PointToScreen(e.GetPosition(this));
        double deltaX = (currentScreen.X - pointerStartScreen.Value.X) / pointerStartDpiScaleX;
        double deltaY = (currentScreen.Y - pointerStartScreen.Value.Y) / pointerStartDpiScaleY;

        if (pointerResizeEdges != ResizeEdges.None)
        {
            if (!manualResizeStarted && !PassedDragThreshold(deltaX, deltaY))
            {
                return;
            }

            if (!manualResizeStarted)
            {
                manualResizeStarted = true;
                AppLog.SetCurrentOperation($"resizing preview: {client.DisplayName}");
                AppLog.Information(
                    "PreviewWindow",
                    $"Manual resize started for {client.DisplayName}; bounds={FormatBounds(pointerStartBounds)}; edges={pointerResizeEdges}.");
            }

            ApplyPointerResize(deltaX, deltaY);
            e.Handled = true;
            return;
        }

        if (!manualMoveStarted)
        {
            if (!PassedDragThreshold(deltaX, deltaY))
            {
                return;
            }

            manualMoveStarted = true;
            AppLog.SetCurrentOperation($"moving preview: {client.DisplayName}");
            AppLog.Information(
                "PreviewWindow",
                $"Manual move started for {client.DisplayName}; bounds={FormatBounds(pointerStartBounds)}.");
        }

        Left = pointerStartBounds.Left + deltaX;
        Top = pointerStartBounds.Top + deltaY;
        e.Handled = true;
    }

    private async void OnSurfacePreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !pointerInteractionActive)
        {
            return;
        }

        bool shouldActivate = !manualMoveStarted && !manualResizeStarted;
        FinishPointerInteraction(commitLayout: manualMoveStarted || manualResizeStarted);
        e.Handled = true;

        if (!shouldActivate)
        {
            return;
        }

        try
        {
            AppLog.Information(
                "Activation",
                $"Preview click requested activation of {client.DisplayName}; source HWND=0x{client.SourceWindowId:X}.");
            await activateClient(client.SourceWindowId);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "Activation",
                $"Preview click activation failed for {client.DisplayName}.",
                exception);
        }
    }

    private void OnSurfaceLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (pointerInteractionActive)
        {
            FinishPointerInteraction(
                commitLayout: manualMoveStarted || manualResizeStarted,
                releaseCapture: false);
        }
    }

    private void FinishPointerInteraction(bool commitLayout, bool releaseCapture = true)
    {
        if (!pointerInteractionActive)
        {
            return;
        }

        Rect finalBounds = CaptureBounds();
        bool wasManualMove = manualMoveStarted;
        bool wasManualResize = manualResizeStarted;
        bool changed = PositionChanged(pointerStartBounds, finalBounds) ||
                       SizeChanged(pointerStartBounds, finalBounds);

        pointerInteractionActive = false;
        manualMoveStarted = false;
        manualResizeStarted = false;
        pointerResizeEdges = ResizeEdges.None;
        pointerStartScreen = null;
        Cursor = null;

        if (releaseCapture && IsMouseCaptured)
        {
            Mouse.Capture(null);
        }

        if (wasManualMove)
        {
            AppLog.Information(
                "PreviewWindow",
                $"Manual move completed for {client.DisplayName}; bounds={FormatBounds(finalBounds)}.");
            AppLog.SetCurrentOperation("idle");
        }
        else if (wasManualResize)
        {
            AppLog.Information(
                "PreviewWindow",
                $"Manual resize completed for {client.DisplayName}; bounds={FormatBounds(finalBounds)}.");
            AppLog.SetCurrentOperation("idle");
        }

        if (!commitLayout || !changed || !layoutTrackingEnabled || suppressLayoutTracking)
        {
            return;
        }

        CommitLayout(finalBounds);
    }

    private void ApplyPointerResize(double deltaX, double deltaY)
    {
        double left = pointerStartBounds.Left;
        double top = pointerStartBounds.Top;
        double width = pointerStartBounds.Width;
        double height = pointerStartBounds.Height;

        if (pointerResizeEdges.HasFlag(ResizeEdges.Left))
        {
            left = pointerStartBounds.Left + deltaX;
            width = pointerStartBounds.Width - deltaX;
            if (width < MinWidth)
            {
                width = MinWidth;
                left = pointerStartBounds.Right - width;
            }
        }
        else if (pointerResizeEdges.HasFlag(ResizeEdges.Right))
        {
            width = Math.Max(MinWidth, pointerStartBounds.Width + deltaX);
        }

        if (pointerResizeEdges.HasFlag(ResizeEdges.Top))
        {
            top = pointerStartBounds.Top + deltaY;
            height = pointerStartBounds.Height - deltaY;
            if (height < MinHeight)
            {
                height = MinHeight;
                top = pointerStartBounds.Bottom - height;
            }
        }
        else if (pointerResizeEdges.HasFlag(ResizeEdges.Bottom))
        {
            height = Math.Max(MinHeight, pointerStartBounds.Height + deltaY);
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    private bool PassedDragThreshold(double deltaX, double deltaY) =>
        Math.Abs(deltaX) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(deltaY) >= SystemParameters.MinimumVerticalDragDistance;

    private ResizeEdges GetResizeEdges(Point point)
    {
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;
        ResizeEdges edges = ResizeEdges.None;

        if (point.X <= ResizeBorderDip)
        {
            edges |= ResizeEdges.Left;
        }
        else if (point.X >= width - ResizeBorderDip)
        {
            edges |= ResizeEdges.Right;
        }

        if (point.Y <= ResizeBorderDip)
        {
            edges |= ResizeEdges.Top;
        }
        else if (point.Y >= height - ResizeBorderDip)
        {
            edges |= ResizeEdges.Bottom;
        }

        return edges;
    }

    private static Cursor? GetResizeCursor(ResizeEdges edges)
    {
        bool horizontal = edges.HasFlag(ResizeEdges.Left) || edges.HasFlag(ResizeEdges.Right);
        bool vertical = edges.HasFlag(ResizeEdges.Top) || edges.HasFlag(ResizeEdges.Bottom);

        if (horizontal && vertical)
        {
            bool northwestSoutheast =
                (edges.HasFlag(ResizeEdges.Left) && edges.HasFlag(ResizeEdges.Top)) ||
                (edges.HasFlag(ResizeEdges.Right) && edges.HasFlag(ResizeEdges.Bottom));
            return northwestSoutheast ? Cursors.SizeNWSE : Cursors.SizeNESW;
        }

        if (horizontal)
        {
            return Cursors.SizeWE;
        }

        return vertical ? Cursors.SizeNS : null;
    }

    private void CommitLayout(Rect bounds) =>
        LayoutCommitted?.Invoke(
            this,
            new PreviewWindowLayoutChangedEventArgs(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height));

    private Rect CaptureBounds()
    {
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;
        return new Rect(Left, Top, width, height);
    }

    private static bool PositionChanged(Rect before, Rect after) =>
        Math.Abs(before.Left - after.Left) > 0.5 ||
        Math.Abs(before.Top - after.Top) > 0.5;

    private static bool SizeChanged(Rect before, Rect after) =>
        Math.Abs(before.Width - after.Width) > 0.5 ||
        Math.Abs(before.Height - after.Height) > 0.5;

    private static string FormatBounds(Rect bounds) =>
        $"{bounds.Left:0},{bounds.Top:0} {bounds.Width:0}x{bounds.Height:0}";

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (pointerInteractionActive)
        {
            FinishPointerInteraction(
                commitLayout: manualMoveStarted || manualResizeStarted);
        }

        SourceInitialized -= OnSourceInitialized;
        Closed -= OnWindowClosed;
        AppLog.Information("PreviewWindow", $"Preview window closed for {client.DisplayName}.");
    }

    [Flags]
    private enum ResizeEdges
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
    }
}
