using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.Presentation;

public partial class FloatingPreviewWindow
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmWindowCornerPreferenceDoNotRound = 1;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private FloatingPreviewOverlayWindow? textOverlay;
    private DispatcherOperation? overlaySyncOperation;
    private bool overlayCloseHooked;
    private bool overlaySizeHooked;
    private bool overlayOwnerClosed;

    private void OnPreviewWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (overlayOwnerClosed)
        {
            return;
        }

        SuppressNativeWindowFrame();
        EnsureTextOverlay();
        QueueOverlaySync();

        if (!overlayCloseHooked)
        {
            overlayCloseHooked = true;
            Closed += OnPreviewOwnerClosed;
        }

        if (!overlaySizeHooked)
        {
            overlaySizeHooked = true;
            AddHandler(
                FrameworkElement.SizeChangedEvent,
                new SizeChangedEventHandler(OnPreviewWindowSizeChanged),
                handledEventsToo: true);
        }
    }

    private void OnPreviewWindowLocationChanged(object? sender, EventArgs e) => QueueOverlaySync();

    private void OnPreviewWindowSizeChanged(object sender, SizeChangedEventArgs e) => QueueOverlaySync();

    private void OnPreviewWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        QueueOverlaySync();

    private void OnPreviewWindowStateChanged(object? sender, EventArgs e) => QueueOverlaySync();

    private bool EnsureTextOverlay()
    {
        if (overlayOwnerClosed || !IsLoaded)
        {
            return false;
        }

        if (textOverlay is not null)
        {
            return true;
        }

        nint ownerHandle = new WindowInteropHelper(this).Handle;
        if (ownerHandle == nint.Zero)
        {
            return false;
        }

        try
        {
            textOverlay = new FloatingPreviewOverlayWindow(this, DataContext);
            BindingOperations.SetBinding(
                textOverlay,
                TopmostProperty,
                new Binding(nameof(Topmost)) { Source = this, Mode = BindingMode.OneWay });
            textOverlay.Show();
            AppLog.Information("PreviewOverlay", $"Text overlay created for {client.DisplayName}.");
            return true;
        }
        catch (Exception exception)
        {
            textOverlay = null;
            AppLog.Warning(
                "PreviewOverlay",
                $"Could not create text overlay for {client.DisplayName}.",
                exception);
            return false;
        }
    }

    private void QueueOverlaySync()
    {
        if (overlayOwnerClosed || !IsLoaded)
        {
            return;
        }

        if (overlaySyncOperation is { Status: DispatcherOperationStatus.Pending })
        {
            return;
        }

        overlaySyncOperation = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                overlaySyncOperation = null;
                if (overlayOwnerClosed || !IsLoaded)
                {
                    return;
                }

                try
                {
                    SynchronizeTextOverlay();
                }
                catch (Exception exception)
                {
                    AppLog.Warning(
                        "PreviewOverlay",
                        $"Queued overlay synchronization failed for {client.DisplayName}.",
                        exception);
                }
            }));
    }

    private void SynchronizeTextOverlay()
    {
        if (overlayOwnerClosed || !IsLoaded || !EnsureTextOverlay() || textOverlay is null)
        {
            return;
        }

        bool shouldShow = IsVisible && WindowState == WindowState.Normal;
        if (!shouldShow)
        {
            textOverlay.Hide();
            return;
        }

        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;
        if (width <= 0 || height <= 0 || double.IsNaN(Left) || double.IsNaN(Top))
        {
            return;
        }

        textOverlay.Left = Left;
        textOverlay.Top = Top;
        textOverlay.Width = width;
        textOverlay.Height = height;

        if (!textOverlay.IsVisible)
        {
            textOverlay.Show();
        }
    }

    private void OnPreviewOwnerClosed(object? sender, EventArgs e)
    {
        overlayOwnerClosed = true;

        Closed -= OnPreviewOwnerClosed;
        overlayCloseHooked = false;

        if (overlaySizeHooked)
        {
            RemoveHandler(
                FrameworkElement.SizeChangedEvent,
                new SizeChangedEventHandler(OnPreviewWindowSizeChanged));
            overlaySizeHooked = false;
        }

        if (overlaySyncOperation is { Status: DispatcherOperationStatus.Pending })
        {
            _ = overlaySyncOperation.Abort();
        }

        overlaySyncOperation = null;

        FloatingPreviewOverlayWindow? overlay = textOverlay;
        textOverlay = null;
        if (overlay is not null)
        {
            try
            {
                overlay.Close();
                AppLog.Information("PreviewOverlay", $"Text overlay closed for {client.DisplayName}.");
            }
            catch (Exception exception)
            {
                AppLog.Warning(
                    "PreviewOverlay",
                    $"Could not close text overlay for {client.DisplayName}.",
                    exception);
            }
        }
    }

    private void SuppressNativeWindowFrame()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        try
        {
            uint borderColor = DwmColorNone;
            _ = DwmSetWindowAttribute(
                handle,
                DwmwaBorderColor,
                ref borderColor,
                Marshal.SizeOf<uint>());

            int cornerPreference = DwmWindowCornerPreferenceDoNotRound;
            _ = DwmSetWindowAttribute(
                handle,
                DwmwaWindowCornerPreference,
                ref cornerPreference,
                Marshal.SizeOf<int>());

            _ = SetWindowPos(
                handle,
                nint.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        catch (Exception exception)
        {
            AppLog.Warning("PreviewWindow", "Could not suppress the native preview border.", exception);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref uint value,
        int valueSize);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
