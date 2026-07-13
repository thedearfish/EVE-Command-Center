using EveCommandCenter.Application.Cycling;
using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Application.Abstractions;

public interface IWindowActivationService
{
    Task<ActivationResult> ActivateAsync(
        WindowId windowId,
        CancellationToken cancellationToken = default);
}
