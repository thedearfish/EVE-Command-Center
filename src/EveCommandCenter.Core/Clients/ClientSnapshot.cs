namespace EveCommandCenter.Core.Clients;

public sealed record ClientSnapshot(
    ClientId Id,
    ClientSessionId SessionId,
    WindowId WindowId,
    string DisplayName,
    ClientState State);
