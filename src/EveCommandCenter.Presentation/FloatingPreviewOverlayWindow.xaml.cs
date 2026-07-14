using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EveCommandCenter.Presentation;

public partial class FloatingPreviewOverlayWindow : Window
{
    private const int GwlExStyle = -20;
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
        Owner = previewOwner;
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
}
