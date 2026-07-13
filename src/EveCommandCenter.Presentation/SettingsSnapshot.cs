namespace EveCommandCenter.Presentation;

public sealed record SettingsSnapshot(
    bool AutoCreatePreviews,
    bool AlwaysOnTop,
    bool ShowPreviewHeader,
    int PreviewWidth,
    int PreviewHeight,
    double PreviewOpacity,
    string NextCharacterHotkey,
    string PreviousCharacterHotkey,
    string TogglePreviewsHotkey);
