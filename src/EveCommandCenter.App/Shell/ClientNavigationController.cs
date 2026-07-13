using EveCommandCenter.Application.Abstractions;
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
        var result = await activationService.ActivateAsync(new WindowId(target.SourceWindowId));
        viewModel.SetSettingsResult(result.IsSuccess
            ? $"Activated {target.DisplayName}."
            : result.Message ?? $"Could not activate {target.DisplayName}.");
    }
}
