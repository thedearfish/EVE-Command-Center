using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.Presentation;

public static class NativeWindowOpacityBehavior
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;

    public static readonly DependencyProperty OpacityProperty = DependencyProperty.RegisterAttached(
        "Opacity",
        typeof(double),
        typeof(NativeWindowOpacityBehavior),
        new PropertyMetadata(1.0, OnOpacityChanged));

    private static readonly DependencyProperty IsHookedProperty = DependencyProperty.RegisterAttached(
        "IsHooked",
        typeof(bool),
        typeof(NativeWindowOpacityBehavior),
        new PropertyMetadata(false));

    public static double GetOpacity(DependencyObject element) =>
        (double)element.GetValue(OpacityProperty);

    public static void SetOpacity(DependencyObject element, double value) =>
        element.SetValue(OpacityProperty, Math.Clamp(value, 0.05, 1.0));

    private static void OnOpacityChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Window window)
        {
            return;
        }

        // WPF Opacity does not reliably include DWM thumbnails. Keep WPF fully opaque
        // and apply alpha to the complete native destination HWND instead.
        window.Opacity = 1.0;

        if (!(bool)window.GetValue(IsHookedProperty))
        {
            window.SetValue(IsHookedProperty, true);
            window.SourceInitialized += OnWindowSourceInitialized;
        }

        Apply(window);
    }

    private static void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            Apply(window);
        }
    }

    private static void Apply(Window window)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        try
        {
            nint style = GetWindowLongPtr(handle, GwlExStyle);
            nint layeredStyle = new(style.ToInt64() | WsExLayered);
            if (layeredStyle != style)
            {
                _ = SetWindowLongPtr(handle, GwlExStyle, layeredStyle);
            }

            byte alpha = (byte)Math.Clamp(
                (int)Math.Round(Math.Clamp(GetOpacity(window), 0.05, 1.0) * byte.MaxValue),
                1,
                byte.MaxValue);

            if (!SetLayeredWindowAttributes(handle, 0, alpha, LwaAlpha))
            {
                int error = Marshal.GetLastWin32Error();
                AppLog.Warning("Opacity", $"Could not apply native opacity; HWND=0x{handle.ToInt64():X}; error={error}.");
            }
        }
        catch (Exception exception)
        {
            AppLog.Warning("Opacity", "Could not apply native preview opacity.", exception);
        }
    }

    private static nint GetWindowLongPtr(nint windowHandle, int index) =>
        Environment.Is64BitProcess
            ? GetWindowLongPtr64(windowHandle, index)
            : new nint(GetWindowLong32(windowHandle, index));

    private static nint SetWindowLongPtr(nint windowHandle, int index, nint value) =>
        Environment.Is64BitProcess
            ? SetWindowLongPtr64(windowHandle, index, value)
            : new nint(SetWindowLong32(windowHandle, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(nint windowHandle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(
        nint windowHandle,
        uint colorKey,
        byte alpha,
        uint flags);
}
