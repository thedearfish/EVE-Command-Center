using EveCommandCenter.Application.Abstractions;
using EveCommandCenter.Application.Diagnostics;
using EveCommandCenter.Core.Clients;
using EveCommandCenter.Presentation;
using EveCommandCenter.Windows.Activation;

namespace EveCommandCenter.App.Shell;

public sealed class ClientNavigationController(
    MainWindowViewModel viewModel,
    IWindowActivationService activationService,
    Win32ForegroundWindowSource foregroundWindowSource)
{
    public Task NextAsync() => CycleAsync(1);

    public Task PreviousAsync() => CycleAsync(-1);

    private async Task CycleAsync(int direction)
    {
        List<DetectedClientViewModel> clients = viewModel.Clients
            .Where(client => client.IsCycleEligible)
            .ToList();

        if (clients.Count == 0)
        {
            AppLog.Warning("Navigation", "Character switching requested, but no eligible EVE clients are available.");
            viewModel.SetSettingsResult("No logged-in EVE characters are available for switching.");
            return;
        }

        WindowId foregroundWindow = foregroundWindowSource.GetForegroundWindowId();
        int currentIndex = clients.FindIndex(client => client.SourceWindowId == foregroundWindow.Value);

        int targetIndex;
        if (currentIndex < 0)
        {
            targetIndex = direction > 0 ? 0 : clients.Count - 1;
        }
        else
        {
            targetIndex = (currentIndex + direction + clients.Count) % clients.Count;
        }

        DetectedClientViewModel target = clients[targetIndex];
        AppLog.Information(
            "Navigation",
            $"Character switch requested; direction={direction}; foreground=0x{foregroundWindow.Value:X}; " +
            $"target={target.DisplayName}; targetHwnd=0x{target.SourceWindowId:X}.");

        var result = await activationService.ActivateAsync(new WindowId(target.SourceWindowId));
        viewModel.SetSettingsResult(result.IsSuccess
            ? $"Activated {target.DisplayName}."
            : result.Message ?? $"Could not activate {target.DisplayName}.");

        if (result.IsSuccess)
        {
            AppLog.Information("Navigation", $"Character switch completed: {target.DisplayName}.");
        }
        else
        {
            AppLog.Warning(
                "Navigation",
                $"Character switch failed for {target.DisplayName}: {result.Message}");
        }
    }
}
