namespace EveCommandCenter.Presentation;

public sealed record DetectedClientViewModel(
    string DisplayName,
    string State,
    int ProcessId,
    string WindowId,
    string WindowTitle,
    bool IsVisible,
    bool IsMinimized,
    bool IsResponsive,
    bool IsCycleEligible);
