using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using EveCommandCenter.Application.Discovery;
using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Windows.Discovery;

public sealed class Win32WindowSnapshotSource : IWindowSnapshotSource
{
    private const uint WmNull = 0x0000;
    private const uint SmtoAbortIfHung = 0x0002;

    public IReadOnlyList<WindowCandidate> Capture()
    {
        List<WindowCandidate> windows = [];

        _ = EnumWindows((windowHandle, _) =>
        {
            WindowCandidate? candidate = TryCreateCandidate(windowHandle);
            if (candidate is not null)
            {
                windows.Add(candidate);
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static WindowCandidate? TryCreateCandidate(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(windowHandle, out uint processId);
        if (processId == 0 || processId > int.MaxValue)
        {
            return null;
        }

        string processName;
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return new WindowCandidate(
            new WindowId(windowHandle.ToInt64()),
            (int)processId,
            processName,
            ReadWindowTitle(windowHandle),
            IsWindowVisible(windowHandle),
            IsIconic(windowHandle),
            IsResponsive(windowHandle));
    }

    private static string ReadWindowTitle(IntPtr windowHandle)
    {
        int length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(length + 1);
        _ = GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool IsResponsive(IntPtr windowHandle) =>
        SendMessageTimeout(
            windowHandle,
            WmNull,
            IntPtr.Zero,
            IntPtr.Zero,
            SmtoAbortIfHung,
            100,
            out _) != IntPtr.Zero;

    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter,
        uint flags,
        uint timeoutMilliseconds,
        out IntPtr result);
}
