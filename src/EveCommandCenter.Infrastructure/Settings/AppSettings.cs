namespace EveCommandCenter.Infrastructure.Settings;

public sealed record AppSettings
{
    public CycleSettings Cycle { get; init; } = new();

    public PreviewSettings Preview { get; init; } = new();

    public GeneralSettings General { get; init; } = new();
}

public sealed record CycleSettings
{
    public IReadOnlyList<Guid> ClientOrder { get; init; } = Array.Empty<Guid>();

    public bool SkipCharacterSelection { get; init; } = true;

    public bool SkipClosedClients { get; init; } = true;
}

public sealed record PreviewSettings
{
    public int ThumbnailWidth { get; init; } = 320;

    public int ThumbnailHeight { get; init; } = 180;
}

public sealed record GeneralSettings
{
    public bool StartMinimized { get; init; }

    public bool MinimizeToTray { get; init; } = true;
}
