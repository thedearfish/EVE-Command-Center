using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
    private const int WmNcHitTest = 0x0084;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;

    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

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

    private readonly DetectedClientViewModel client;
    private readonly Func<long, Task> activateClient;
    private HwndSource? hwndSource;
    private Rect interactionStartBounds;
    private bool nativeInteractionActive;
    private bool layoutTrackingEnabled;
    private bool suppressLayoutTracking;

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
        hwndSource = HwndSource.FromHwnd(handle);
        hwndSource?.AddHook(WindowProcedure);
        AppLog.Information(
            "PreviewWindow",
            $"Native preview window initialized for {client.DisplayName}; HWND=0x{handle.ToInt64():X}.");
    }

    private nint WindowProcedure(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        switch (message)
        {
            case WmNcHitTest:
            {
                int hitTest = HitTestResizeBorder(hwnd, lParam);
                if (hitTest != HtClient)
                {
                    handled = true;
                    return new nint(hitTest);
                }

                break;
            }

            case WmEnterSizeMove:
                BeginNativeInteraction();
                break;

            case WmExitSizeMove:
                EndNativeInteraction();
                break;
        }

        return nint.Zero;
    }

    private int HitTestResizeBorder(nint hwnd, nint lParam)
    {
        if (ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize ||
            WindowState != WindowState.Normal ||
            !GetWindowRect(hwnd, out NativeRect rectangle))
        {
            return HtClient;
        }

        int cursorX = GetSignedLowWord(lParam);
        int cursorY = GetSignedHighWord(lParam);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;
        int border = Math.Max(6, (int)Math.Ceiling(ResizeBorderDip * scale));

        bool left = cursorX >= rectangle.Left && cursorX < rectangle.Left + border;
        bool right = cursorX <= rectangle.Right && cursorX > rectangle.Right - border;
        bool top = cursorY >= rectangle.Top && cursorY < rectangle.Top + border;
        bool bottom = cursorY <= rectangle.Bottom && cursorY > rectangle.Bottom - border;

        if (top && left)
        {
            return HtTopLeft;
        }

        if (top && right)
        {
            return HtTopRight;
        }

        if (bottom && left)
        {
            return HtBottomLeft;
        }

        if (bottom && right)
        {
            return HtBottomRight;
        }

        if (left)
        {
            return HtLeft;
        }

        if (right)
        {
            return HtRight;
        }

        if (top)
        {
            return HtTop;
        }

        if (bottom)
        {
            return HtBottom;
        }

        return HtClient;
    }

    private async void OnSurfacePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            e.LeftButton != MouseButtonState.Pressed ||
            IsInsideResizeBorder(e.GetPosition(this)))
        {
            return;
        }

        e.Handled = true;
        Rect beforeMove = CaptureBounds();

        try
        {
            DragMove();
        }
        catch (InvalidOperationException exception)
        {
            AppLog.Warning(
                "PreviewWindow",
                $"DragMove was rejected for {client.DisplayName}.",
                exception);
            return;
        }

        Rect afterMove = CaptureBounds();
        if (!PositionChanged(beforeMove, afterMove))
        {
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
    }

    private void BeginNativeInteraction()
    {
        nativeInteractionActive = true;
        interactionStartBounds = CaptureBounds();
        AppLog.SetCurrentOperation($"moving/resizing preview: {client.DisplayName}");
        AppLog.Information(
            "PreviewWindow",
            $"Move/resize started for {client.DisplayName}; " +
            $"bounds={FormatBounds(interactionStartBounds)}.");
    }

    private void EndNativeInteraction()
    {
        if (!nativeInteractionActive)
        {
            return;
        }

        nativeInteractionActive = false;
        Rect finalBounds = CaptureBounds();
        bool moved = PositionChanged(interactionStartBounds, finalBounds);
        bool resized = SizeChanged(interactionStartBounds, finalBounds);
        string operation = moved && resized
            ? "move and resize"
            : resized
                ? "resize"
                : moved
                    ? "move"
                    : "interaction";

        AppLog.Information(
            "PreviewWindow",
            $"{operation} completed for {client.DisplayName}; " +
            $"bounds={FormatBounds(finalBounds)}.");
        AppLog.SetCurrentOperation("idle");

        if (!layoutTrackingEnabled || suppressLayoutTracking || (!moved && !resized))
        {
            return;
        }

        LayoutCommitted?.Invoke(
            this,
            new PreviewWindowLayoutChangedEventArgs(
                finalBounds.Left,
                finalBounds.Top,
                finalBounds.Width,
                finalBounds.Height));
    }

    private bool IsInsideResizeBorder(Point point)
    {
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;

        return point.X <= ResizeBorderDip ||
               point.X >= width - ResizeBorderDip ||
               point.Y <= ResizeBorderDip ||
               point.Y >= height - ResizeBorderDip;
    }

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
        if (nativeInteractionActive)
        {
            AppLog.Warning(
                "PreviewWindow",
                $"Window for {client.DisplayName} closed during a native move/resize interaction.");
            AppLog.SetCurrentOperation("idle");
        }

        hwndSource?.RemoveHook(WindowProcedure);
        hwndSource = null;
        SourceInitialized -= OnSourceInitialized;
        Closed -= OnWindowClosed;
        AppLog.Information("PreviewWindow", $"Preview window closed for {client.DisplayName}.");
    }

    private static int GetSignedLowWord(nint value) =>
        unchecked((short)((long)value & 0xFFFF));

    private static int GetSignedHighWord(nint value) =>
        unchecked((short)(((long)value >> 16) & 0xFFFF));

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
