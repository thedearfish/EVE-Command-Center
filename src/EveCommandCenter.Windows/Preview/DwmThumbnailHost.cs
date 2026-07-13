using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Windows.Preview;

public sealed class DwmThumbnailHost : FrameworkElement, IDisposable
{
    public static readonly DependencyProperty SourceWindowIdProperty = DependencyProperty.Register(
        nameof(SourceWindowId),
        typeof(long),
        typeof(DwmThumbnailHost),
        new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceWindowChanged));

    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText),
        typeof(string),
        typeof(DwmThumbnailHost),
        new PropertyMetadata(string.Empty));

    private DwmThumbnailSession? session;
    private Window? ownerWindow;
    private bool disposed;

    public DwmThumbnailHost()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        LayoutUpdated += OnLayoutUpdated;
    }

    public long SourceWindowId
    {
        get => (long)GetValue(SourceWindowIdProperty);
        set => SetValue(SourceWindowIdProperty, value);
    }

    public string ErrorText
    {
        get => (string)GetValue(ErrorTextProperty);
        private set => SetValue(ErrorTextProperty, value);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        SizeChanged -= OnSizeChanged;
        LayoutUpdated -= OnLayoutUpdated;
        DisposeSession();
    }

    private static void OnSourceWindowChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var host = (DwmThumbnailHost)dependencyObject;
        host.RecreateSession();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ownerWindow = Window.GetWindow(this);
        RecreateSession();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DisposeSession();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateThumbnail();

    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdateThumbnail();

    private void RecreateSession()
    {
        DisposeSession();
        ErrorText = string.Empty;

        if (!IsLoaded || SourceWindowId == 0 || ownerWindow is null)
        {
            return;
        }

        try
        {
            nint destinationHandle = new WindowInteropHelper(ownerWindow).Handle;
            if (destinationHandle == 0)
            {
                return;
            }

            session = DwmThumbnailSession.Register(
                new WindowId(destinationHandle.ToInt64()),
                new WindowId(SourceWindowId));

            UpdateThumbnail();
        }
        catch (Win32Exception exception)
        {
            ErrorText = $"DWM preview unavailable: 0x{exception.NativeErrorCode:X8}";
            DisposeSession();
        }
        catch (Exception exception)
        {
            ErrorText = $"DWM preview unavailable: {exception.Message}";
            DisposeSession();
        }
    }

    private void UpdateThumbnail()
    {
        if (session is null || ownerWindow is null || ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        try
        {
            nint destinationHandle = new WindowInteropHelper(ownerWindow).Handle;
            if (destinationHandle == 0)
            {
                return;
            }

            Point topLeftScreen = PointToScreen(new Point(0, 0));
            Point bottomRightScreen = PointToScreen(new Point(ActualWidth, ActualHeight));

            var topLeft = new NativePoint((int)Math.Round(topLeftScreen.X), (int)Math.Round(topLeftScreen.Y));
            var bottomRight = new NativePoint((int)Math.Round(bottomRightScreen.X), (int)Math.Round(bottomRightScreen.Y));

            if (!ScreenToClient(destinationHandle, ref topLeft) ||
                !ScreenToClient(destinationHandle, ref bottomRight))
            {
                return;
            }

            int availableWidth = Math.Max(0, bottomRight.X - topLeft.X);
            int availableHeight = Math.Max(0, bottomRight.Y - topLeft.Y);
            if (availableWidth == 0 || availableHeight == 0)
            {
                return;
            }

            DwmThumbnailSize sourceSize = session.GetSourceSize();
            DwmThumbnailBounds fitted = DwmThumbnailLayout.Fit(
                sourceSize,
                new DwmThumbnailBounds(topLeft.X, topLeft.Y, availableWidth, availableHeight));

            if (fitted.Width > 0 && fitted.Height > 0)
            {
                session.Update(fitted);
                ErrorText = string.Empty;
            }
        }
        catch (Exception exception)
        {
            ErrorText = $"Preview update failed: {exception.Message}";
        }
    }

    private void DisposeSession()
    {
        session?.Dispose();
        session = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint windowHandle, ref NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }
}