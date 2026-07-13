namespace EveCommandCenter.Core.Clients;

public enum ClientState
{
    Discovered = 0,
    CharacterSelection = 1,
    Ready = 2,
    Minimized = 3,
    Unresponsive = 4,
    Closing = 5,
    Closed = 6,
}
