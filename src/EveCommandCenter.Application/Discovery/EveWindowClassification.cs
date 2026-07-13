namespace EveCommandCenter.Application.Discovery;

public enum EveWindowKind
{
    NotEve = 0,
    CharacterSelection = 1,
    Character = 2
}

public sealed record EveWindowClassification(
    EveWindowKind Kind,
    string DisplayName)
{
    public bool IsEve => Kind != EveWindowKind.NotEve;
}
