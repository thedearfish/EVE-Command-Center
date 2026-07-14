namespace EveCommandCenter.Presentation;

public enum PreviewContentMode
{
    Standard = 0,
    ImageOnly = 1,
    TextOnly = 2,
}

public sealed record PreviewModeOption(
    PreviewContentMode Value,
    string DisplayName,
    string Description);
