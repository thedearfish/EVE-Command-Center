using System.Runtime.InteropServices;
using EveCommandCenter.Application.Abstractions;
using EveCommandCenter.Application.Cycling;
using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Windows.Activation;

public sealed class Win32WindowActivationService : IWindowActivationService
{
    private const int SwRestore = 9;

    public Task<ActivationResult> ActivateAsync(
        WindowId windowId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (windowId.IsEmpty)
        {
            return Task.FromResult(ActivationResult.WindowUnavailable());
        }

        var handle = new IntPtr(windowId.Value);

        if (!NativeMethods.IsWindow(handle))
        {
            return Task.FromResult(
                ActivationResult.WindowUnavailable("The native window no longer exists."));
        }

        if (NativeMethods.IsIconic(handle))
        {
            _ = NativeMethods.ShowWindowAsync(handle, SwRestore);
        }

        return Task.FromResult(
            NativeMethods.SetForegroundWindow(handle)
                ? ActivationResult.Success()
                : ActivationResult.Denied());
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr windowHandle);
    }
}
