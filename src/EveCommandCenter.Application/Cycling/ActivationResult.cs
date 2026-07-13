namespace EveCommandCenter.Application.Cycling;

public enum ActivationStatus
{
    Succeeded = 0,
    NoEligibleClients = 1,
    WindowUnavailable = 2,
    Denied = 3,
    Failed = 4,
}

public sealed record ActivationResult(
    ActivationStatus Status,
    string? Message = null)
{
    public bool IsSuccess => Status == ActivationStatus.Succeeded;

    public static ActivationResult Success() => new(ActivationStatus.Succeeded);

    public static ActivationResult NoEligibleClients() =>
        new(ActivationStatus.NoEligibleClients, "No eligible clients are available.");

    public static ActivationResult WindowUnavailable(string? message = null) =>
        new(ActivationStatus.WindowUnavailable, message ?? "The client window is unavailable.");

    public static ActivationResult Denied(string? message = null) =>
        new(ActivationStatus.Denied, message ?? "Windows denied foreground activation.");

    public static ActivationResult Failed(string message) =>
        new(ActivationStatus.Failed, message);
}
