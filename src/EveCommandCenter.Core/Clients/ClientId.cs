namespace EveCommandCenter.Core.Clients;

public readonly record struct ClientId(Guid Value)
{
    public static ClientId New() => new(Guid.NewGuid());

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("D");
}
