namespace EveCommandCenter.Core.Clients;

public readonly record struct ClientSessionId(Guid Value)
{
    public static ClientSessionId New() => new(Guid.NewGuid());

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("D");
}
