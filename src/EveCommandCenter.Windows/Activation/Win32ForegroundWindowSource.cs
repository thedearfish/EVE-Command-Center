using System.Runtime.InteropServices;
using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Windows.Activation;

public sealed class Win32ForegroundWindowSource
{
    public WindowId GetForegroundWindowId()
    {
        nint handle = GetForegroundWindow();
        return new WindowId(handle.ToInt64());
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
