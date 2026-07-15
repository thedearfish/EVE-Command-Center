using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.Presentation;

public static class BorderlessServiceWindowBehavior
{
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(BorderlessServiceWindowBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Window window)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            window.SourceInitialized += OnSourceInitialized;
        }
        else
        {
            window.SourceInitialized -= OnSourceInitialized;
        }
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        try
        {
            nint style = GetWindowLongPtr(handle, GwlStyle);
            long borderlessStyle = style.ToInt64() & ~WsCaption & ~WsThickFrame;
            if (borderlessStyle != style.ToInt64())
            {
                _ = SetWindowLongPtr(handle, GwlStyle, new nint(borderlessStyle));
            }

            _ = SetWindowPos(
                handle,
                nint.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

            AppLog.Debug("PreviewWindow", $"Native caption and resize frame removed; HWND=0x{handle.ToInt64():X}.");
        }
        catch (Exception exception)
        {
            AppLog.Warning("PreviewWindow", "Could not remove the native preview frame.", exception);
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
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
