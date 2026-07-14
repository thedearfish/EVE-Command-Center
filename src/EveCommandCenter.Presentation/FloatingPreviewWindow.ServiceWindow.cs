using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.Presentation;

public partial class FloatingPreviewWindow
{
    private const int ServiceWmSize = 0x0005;
    private const int ServiceWmWindowPosChanging = 0x0046;
    private const int ServiceWmSysCommand = 0x0112;

    private const int ServiceSizeMinimized = 1;
    private const int ServiceSizeMaximized = 2;
    private const int ServiceScMinimize = 0xF020;
    private const int ServiceScMaximize = 0xF030;

    private const int ServiceGwlStyle = -16;
    private const int ServiceGwlExStyle = -20;
    private const long ServiceWsMinimizeBox = 0x00020000L;
    private const long ServiceWsMaximizeBox = 0x00010000L;
    private const long ServiceWsExToolWindow = 0x00000080L;
    private const long ServiceWsExAppWindow = 0x00040000L;

    private const uint ServiceSwpNoSize = 0x0001;
    private const uint ServiceSwpNoMove = 0x0002;
    private const uint ServiceSwpNoZOrder = 0x0004;
    private const uint ServiceSwpNoActivate = 0x0010;
    private const uint ServiceSwpFrameChanged = 0x0020;

    private HwndSource? serviceWindowSource;
    private Rect lastSafeServiceBounds = Rect.Empty;
    private bool restoringServiceWindow;
    private bool serviceWindowClosed;

    private void OnServiceWindowSourceInitialized(object? sender, EventArgs e)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        serviceWindowSource = HwndSource.FromHwnd(handle);
        serviceWindowSource?.AddHook(ServiceWindowProcedure);
        ApplyServiceWindowStyles(handle);

        Rect initialBounds = CaptureBounds();
        if (IsSafeServiceBounds(initialBounds))
        {
            lastSafeServiceBounds = initialBounds;
        }

        LocationChanged += OnServiceWindowGeometryChanged;
        AddHandler(
            FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler(OnServiceWindowGeometryChanged),
            handledEventsToo: true);
        StateChanged += OnServiceWindowStateChanged;
        Closed += OnServiceWindowClosed;

        AppLog.Information(
            "PreviewWindow",
            $"Service-window mode enabled for {client.DisplayName}; hidden from Alt+Tab and maximize/minimize disabled.");
    }

    private nint ServiceWindowProcedure(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        switch (message)
        {
            case ServiceWmSysCommand:
            {
                int command = unchecked((int)(wParam.ToInt64() & 0xFFF0));
                if (command is ServiceScMinimize or ServiceScMaximize)
                {
                    handled = true;
                    AppLog.Debug(
                        "PreviewWindow",
                        $"Blocked Windows minimize/maximize command for service preview {client.DisplayName}.");
                    QueueServiceWindowRestore();
                    return nint.Zero;
                }

                break;
            }

            case ServiceWmWindowPosChanging:
                RepairInvalidWindowPosition(lParam);
                break;

            case ServiceWmSize:
            {
                int sizeType = wParam.ToInt32();
                if (sizeType is ServiceSizeMinimized or ServiceSizeMaximized)
                {
                    QueueServiceWindowRestore();
                }

                break;
            }
        }

        return nint.Zero;
    }

    private void ApplyServiceWindowStyles(nint handle)
    {
        nint style = ServiceGetWindowLongPtr(handle, ServiceGwlStyle);
        long updatedStyle = style.ToInt64() & ~ServiceWsMinimizeBox & ~ServiceWsMaximizeBox;
        _ = ServiceSetWindowLongPtr(handle, ServiceGwlStyle, new nint(updatedStyle));

        nint extendedStyle = ServiceGetWindowLongPtr(handle, ServiceGwlExStyle);
        long updatedExtendedStyle =
            (extendedStyle.ToInt64() | ServiceWsExToolWindow) & ~ServiceWsExAppWindow;
        _ = ServiceSetWindowLongPtr(handle, ServiceGwlExStyle, new nint(updatedExtendedStyle));

        _ = ServiceSetWindowPos(
            handle,
            nint.Zero,
            0,
            0,
            0,
            0,
            ServiceSwpNoMove |
            ServiceSwpNoSize |
            ServiceSwpNoZOrder |
            ServiceSwpNoActivate |
            ServiceSwpFrameChanged);
    }

    private void RepairInvalidWindowPosition(nint lParam)
    {
        if (lParam == nint.Zero || lastSafeServiceBounds.IsEmpty)
        {
            return;
        }

        ServiceWindowPosition position = Marshal.PtrToStructure<ServiceWindowPosition>(lParam);
        bool invalidPosition = position.X <= -30000 || position.Y <= -30000;
        bool invalidSize = position.Width <= 0 || position.Height <= 0 ||
                           position.Width > 16384 || position.Height > 16384;
        if (!invalidPosition && !invalidSize)
        {
            return;
        }

        position.X = (int)Math.Round(lastSafeServiceBounds.Left);
        position.Y = (int)Math.Round(lastSafeServiceBounds.Top);
        position.Width = Math.Max(1, (int)Math.Round(lastSafeServiceBounds.Width));
        position.Height = Math.Max(1, (int)Math.Round(lastSafeServiceBounds.Height));
        position.Flags &= ~(ServiceSwpNoMove | ServiceSwpNoSize);
        Marshal.StructureToPtr(position, lParam, false);

        AppLog.Warning(
            "PreviewWindow",
            $"Rejected invalid Windows geometry for {client.DisplayName}; " +
            $"restoring {lastSafeServiceBounds.Left:0},{lastSafeServiceBounds.Top:0} " +
            $"{lastSafeServiceBounds.Width:0}x{lastSafeServiceBounds.Height:0}.");
    }

    private void OnServiceWindowGeometryChanged(object? sender, EventArgs e)
    {
        if (serviceWindowClosed || restoringServiceWindow || WindowState != WindowState.Normal)
        {
            return;
        }

        Rect bounds = CaptureBounds();
        if (IsSafeServiceBounds(bounds))
        {
            lastSafeServiceBounds = bounds;
        }
    }

    private void OnServiceWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Normal)
        {
            QueueServiceWindowRestore();
        }
    }

    private void QueueServiceWindowRestore()
    {
        if (serviceWindowClosed || restoringServiceWindow)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            new Action(EnsureServiceWindowNormalState));
    }

    private void EnsureServiceWindowNormalState()
    {
        if (serviceWindowClosed || restoringServiceWindow)
        {
            return;
        }

        Rect currentBounds = CaptureBounds();
        if (WindowState == WindowState.Normal && IsSafeServiceBounds(currentBounds))
        {
            lastSafeServiceBounds = currentBounds;
            return;
        }

        restoringServiceWindow = true;
        try
        {
            WindowState = WindowState.Normal;
            if (!lastSafeServiceBounds.IsEmpty)
            {
                ApplyBounds(
                    lastSafeServiceBounds.Left,
                    lastSafeServiceBounds.Top,
                    lastSafeServiceBounds.Width,
                    lastSafeServiceBounds.Height);
            }

            AppLog.Warning(
                "PreviewWindow",
                $"Restored service preview {client.DisplayName} after an unwanted Windows snap/minimize state.");
        }
        finally
        {
            restoringServiceWindow = false;
        }
    }

    private void OnServiceWindowClosed(object? sender, EventArgs e)
    {
        serviceWindowClosed = true;
        LocationChanged -= OnServiceWindowGeometryChanged;
        RemoveHandler(
            FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler(OnServiceWindowGeometryChanged));
        StateChanged -= OnServiceWindowStateChanged;
        Closed -= OnServiceWindowClosed;

        serviceWindowSource?.RemoveHook(ServiceWindowProcedure);
        serviceWindowSource = null;
    }

    private static bool IsSafeServiceBounds(Rect bounds) =>
        !bounds.IsEmpty &&
        !double.IsNaN(bounds.Left) &&
        !double.IsNaN(bounds.Top) &&
        !double.IsNaN(bounds.Width) &&
        !double.IsNaN(bounds.Height) &&
        bounds.Left > -30000 &&
        bounds.Top > -30000 &&
        bounds.Width >= 32 &&
        bounds.Height >= 24 &&
        bounds.Width <= 16384 &&
        bounds.Height <= 16384;

    private static nint ServiceGetWindowLongPtr(nint windowHandle, int index) =>
        Environment.Is64BitProcess
            ? ServiceGetWindowLongPtr64(windowHandle, index)
            : new nint(ServiceGetWindowLong32(windowHandle, index));

    private static nint ServiceSetWindowLongPtr(nint windowHandle, int index, nint value) =>
        Environment.Is64BitProcess
            ? ServiceSetWindowLongPtr64(windowHandle, index, value)
            : new nint(ServiceSetWindowLong32(windowHandle, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int ServiceGetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint ServiceGetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int ServiceSetWindowLong32(nint windowHandle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern nint ServiceSetWindowLongPtr64(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ServiceSetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceWindowPosition
    {
        public nint WindowHandle;
        public nint InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }
}
