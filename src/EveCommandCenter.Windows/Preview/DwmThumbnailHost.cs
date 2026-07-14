using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EveCommandCenter.Application.Diagnostics;
using EveCommandCenter.Application.Preview;
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

    private void RecreateSession()
    {
        DisposeSession();
        SetError(string.Empty);

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
            AppLog.Information(
                "DWM",
                $"Thumbnail registered; source=0x{SourceWindowId:X}, destination=0x{destinationHandle.ToInt64():X}.");
            UpdateThumbnail();
        }
        catch (Win32Exception exception)
        {
            SetError(
                $"DWM preview unavailable: 0x{exception.NativeErrorCode:X8}",
                exception);
            DisposeSession();
        }
        catch (Exception exception)
        {
            SetError($"DWM preview unavailable: {exception.Message}", exception);
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
            PreviewRectangle fitted = PreviewLayoutCalculator.Fit(
                sourceSize.Width,
                sourceSize.Height,
                new PreviewRectangle(topLeft.X, topLeft.Y, availableWidth, availableHeight));

            if (fitted.Width > 0 && fitted.Height > 0)
            {
                session.Update(new DwmThumbnailBounds(
                    fitted.Left,
                    fitted.Top,
                    fitted.Width,
                    fitted.Height));
                SetError(string.Empty);
            }
        }
        catch (Exception exception)
        {
            SetError($"Preview update failed: {exception.Message}", exception);
        }
    }

    private void SetError(string message, Exception? exception = null)
    {
        if (string.Equals(ErrorText, message, StringComparison.Ordinal))
        {
            return;
        }

        string previous = ErrorText;
        ErrorText = message;

        if (!string.IsNullOrWhiteSpace(message))
        {
            AppLog.Warning(
                "DWM",
                $"{message} Source HWND=0x{SourceWindowId:X}.",
                exception);
        }
        else if (!string.IsNullOrWhiteSpace(previous))
        {
            AppLog.Information(
                "DWM",
                $"Thumbnail rendering recovered for source HWND=0x{SourceWindowId:X}.");
        }
    }

    private void DisposeSession()
    {
        if (session is not null)
        {
            AppLog.Information(
                "DWM",
                $"Thumbnail unregistered for source HWND=0x{SourceWindowId:X}.");
            session.Dispose();
            session = null;
        }
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
