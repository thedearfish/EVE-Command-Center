namespace EveCommandCenter.Core.Clients;

/// <summary>
/// Platform-neutral identifier for a native top-level window.
/// Conversion to or from an HWND belongs in the Windows adapter.
/// </summary>
public readonly record struct WindowId(long Value)
{
    public bool IsEmpty => Value == 0;
}
