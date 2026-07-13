using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Application.Discovery;

public sealed record WindowCandidate(
    WindowId WindowId,
    int ProcessId,
    string ProcessName,
    string Title,
    bool IsVisible,
    bool IsMinimized,
    bool IsResponsive);
