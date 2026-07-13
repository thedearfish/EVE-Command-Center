using EveCommandCenter.Application.Abstractions;
using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Application.Cycling;

public sealed class ClientCycleService(
    IClientRegistry clientRegistry,
    ICycleEligibilityPolicy eligibilityPolicy,
    IWindowActivationService windowActivationService)
{
    public async Task<ActivationResult> CycleAsync(
        ClientId? currentClientId,
        CycleDirection direction,
        IReadOnlyList<ClientId>? preferredOrder = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var orderedClients = OrderClients(
                clientRegistry.GetSnapshot(),
                preferredOrder)
            .Where(eligibilityPolicy.IsEligible)
            .ToArray();

        if (orderedClients.Length == 0)
        {
            return ActivationResult.NoEligibleClients();
        }

        var currentIndex = currentClientId is null
            ? -1
            : Array.FindIndex(orderedClients, client => client.Id == currentClientId.Value);

        var targetIndex = direction switch
        {
            CycleDirection.Next => currentIndex < 0
                ? 0
                : (currentIndex + 1) % orderedClients.Length,
            CycleDirection.Previous => currentIndex < 0
                ? orderedClients.Length - 1
                : (currentIndex - 1 + orderedClients.Length) % orderedClients.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };

        return await windowActivationService
            .ActivateAsync(orderedClients[targetIndex].WindowId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static IEnumerable<ClientSnapshot> OrderClients(
        IReadOnlyList<ClientSnapshot> clients,
        IReadOnlyList<ClientId>? preferredOrder)
    {
        if (preferredOrder is null || preferredOrder.Count == 0)
        {
            return clients.OrderBy(client => client.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        var rank = preferredOrder
            .Select((clientId, index) => (clientId, index))
            .GroupBy(item => item.clientId)
            .ToDictionary(group => group.Key, group => group.First().index);

        return clients
            .OrderBy(client => rank.TryGetValue(client.Id, out var index) ? 0 : 1)
            .ThenBy(client => rank.TryGetValue(client.Id, out var index) ? index : int.MaxValue)
            .ThenBy(client => client.DisplayName, StringComparer.OrdinalIgnoreCase);
    }
}
