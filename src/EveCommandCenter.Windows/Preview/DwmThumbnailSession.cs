using System.ComponentModel;
using System.Runtime.InteropServices;
using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Windows.Preview;

public sealed class DwmThumbnailSession : IDisposable
{
    private const uint DwmTnpRectDestination = 0x00000001;
    private const uint DwmTnpVisible = 0x00000008;
    private const uint DwmTnpOpacity = 0x00000004;
    private const uint DwmTnpSourceClientAreaOnly = 0x00000010;

    private nint _thumbnail;

    private DwmThumbnailSession(nint thumbnail)
    {
        _thumbnail = thumbnail;
    }

    public bool IsDisposed => _thumbnail == 0;

    public static DwmThumbnailSession Register(WindowId destinationWindow, WindowId sourceWindow)
    {
        if (destinationWindow.IsEmpty)
        {
            throw new ArgumentException("Destination window cannot be empty.", nameof(destinationWindow));
        }

        if (sourceWindow.IsEmpty)
        {
            throw new ArgumentException("Source window cannot be empty.", nameof(sourceWindow));
        }

        int result = DwmRegisterThumbnail(
            new nint(destinationWindow.Value),
            new nint(sourceWindow.Value),
            out nint thumbnail);

        if (result != 0)
        {
            throw new Win32Exception(result, "DwmRegisterThumbnail failed.");
        }

        return new DwmThumbnailSession(thumbnail);
    }

    public DwmThumbnailSize GetSourceSize()
    {
        ThrowIfDisposed();

        int result = DwmQueryThumbnailSourceSize(_thumbnail, out NativeSize size);
        if (result != 0)
        {
            throw new Win32Exception(result, "DwmQueryThumbnailSourceSize failed.");
        }

        return new DwmThumbnailSize(size.Width, size.Height);
    }

    public void Update(DwmThumbnailBounds bounds, byte opacity = byte.MaxValue, bool visible = true)
    {
        ThrowIfDisposed();

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Thumbnail bounds must have a positive size.");
        }

        var properties = new DwmThumbnailProperties
        {
            Flags = DwmTnpRectDestination |
                    DwmTnpVisible |
                    DwmTnpOpacity |
                    DwmTnpSourceClientAreaOnly,
            Destination = new NativeRect(
                bounds.Left,
                bounds.Top,
                bounds.Right,
                bounds.Bottom),
            Opacity = opacity,
            Visible = visible,
            SourceClientAreaOnly = true
        };

        int result = DwmUpdateThumbnailProperties(_thumbnail, ref properties);
        if (result != 0)
        {
            throw new Win32Exception(result, "DwmUpdateThumbnailProperties failed.");
        }
    }

    public void Dispose()
    {
        nint thumbnail = Interlocked.Exchange(ref _thumbnail, 0);
        if (thumbnail != 0)
        {
            _ = DwmUnregisterThumbnail(thumbnail);
        }

        GC.SuppressFinalize(this);
    }

    ~DwmThumbnailSession()
    {
        Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_thumbnail == 0, this);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(
        nint destinationWindow,
        nint sourceWindow,
        out nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(
        nint thumbnail,
        out NativeSize size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(
        nint thumbnail,
        ref DwmThumbnailProperties properties);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmThumbnailProperties
    {
        public uint Flags;
        public NativeRect Destination;
        public NativeRect Source;
        public byte Opacity;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Visible;

        [MarshalAs(UnmanagedType.Bool)]
        public bool SourceClientAreaOnly;
    }
}

public readonly record struct DwmThumbnailSize(int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct DwmThumbnailBounds(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);

    public int Bottom => checked(Top + Height);
}
