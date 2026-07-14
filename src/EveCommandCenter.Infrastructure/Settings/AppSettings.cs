namespace EveCommandCenter.Infrastructure.Settings;

public sealed record AppSettings
{
    public CycleSettings Cycle { get; init; } = new();

    public PreviewSettings Preview { get; init; } = new();

    public GeneralSettings General { get; init; } = new();

    public HotkeySettings Hotkeys { get; init; } = new();
}

public sealed record CycleSettings
{
    public IReadOnlyList<Guid> ClientOrder { get; init; } = Array.Empty<Guid>();

    public bool SkipCharacterSelection { get; init; } = true;

    public bool SkipClosedClients { get; init; } = true;
}

public sealed record PreviewSettings
{
    public bool AutoCreate { get; init; } = true;

    public bool AlwaysOnTop { get; init; } = true;

    public bool ShowHeader { get; init; } = true;

    public int ThumbnailWidth { get; init; } = 320;

    public int ThumbnailHeight { get; init; } = 180;

    public double Opacity { get; init; } = 1.0;

    public IReadOnlyList<CharacterPreviewSettings> Characters { get; init; } =
        Array.Empty<CharacterPreviewSettings>();
}

public sealed record CharacterPreviewSettings
{
    public string CharacterName { get; init; } = string.Empty;

    public string CustomLabel { get; init; } = string.Empty;

    public string ContentMode { get; init; } = "Standard";

    public double? Left { get; init; }

    public double? Top { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }
}

public sealed record GeneralSettings
{
    public bool StartMinimized { get; init; } = true;

    public bool MinimizeToTray { get; init; } = true;
}

public sealed record HotkeySettings
{
    public string NextCharacter { get; init; } = "Ctrl+Alt+Right";

    public string PreviousCharacter { get; init; } = "Ctrl+Alt+Left";

    public string TogglePreviews { get; init; } = "Ctrl+Alt+P";
}
