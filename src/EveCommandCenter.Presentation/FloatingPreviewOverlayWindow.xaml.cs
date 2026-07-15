using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.Presentation;

public partial class FloatingPreviewOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int GwlHwndParent = -8;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    public FloatingPreviewOverlayWindow(
        FloatingPreviewWindow previewOwner,
        object dataContext)
    {
        PreviewOwner = previewOwner ?? throw new ArgumentNullException(nameof(previewOwner));

        InitializeComponent();
        DataContext = dataContext;
        SourceInitialized += OnSourceInitialized;
    }

    public FloatingPreviewWindow PreviewOwner { get; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        nint style = GetWindowLongPtr(handle, GwlExStyle);
        nint updated = new(style.ToInt64() | WsExTransparent | WsExToolWindow | WsExNoActivate);
        _ = SetWindowLongPtr(handle, GwlExStyle, updated);

        // Do not use WPF Window.Owner here. The EVE client list can briefly replace a
        // preview window while a queued overlay update is still pending; assigning a
        // closed WPF owner throws on the dispatcher. Native ownership is safe and keeps
        // the click-through label window above its preview without activating it.
        nint ownerHandle = new WindowInteropHelper(PreviewOwner).Handle;
        if (ownerHandle != nint.Zero && IsWindow(ownerHandle))
        {
            _ = SetWindowLongPtr(handle, GwlHwndParent, ownerHandle);
        }
        else
        {
            AppLog.Debug(
                "PreviewOverlay",
                $"Overlay owner handle is unavailable for {PreviewOwner.Title}; native ownership was skipped.");
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);
}
