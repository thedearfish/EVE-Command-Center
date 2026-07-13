using EveCommandCenter.Application.Abstractions;
using EveCommandCenter.Application.Cycling;
using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Application.Tests;

public sealed class ClientCycleServiceTests
{
    [Fact]
    public async Task Next_uses_preferred_order_and_skips_character_selection()
    {
        var first = CreateClient("Alpha", ClientState.Ready, 101);
        var skipped = CreateClient("Character Selection", ClientState.CharacterSelection, 102);
        var second = CreateClient("Bravo", ClientState.Ready, 103);

        var registry = new StubRegistry([first, skipped, second]);
        var activation = new RecordingActivationService();
        var service = new ClientCycleService(
            registry,
            new DefaultCycleEligibilityPolicy(),
            activation);

        var result = await service.CycleAsync(
            first.Id,
            CycleDirection.Next,
            [second.Id, skipped.Id, first.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal(second.WindowId, activation.LastActivatedWindow);
    }

    [Fact]
    public async Task No_eligible_clients_returns_structured_result()
    {
        var client = CreateClient("Closed", ClientState.Closed, 201);
        var registry = new StubRegistry([client]);
        var activation = new RecordingActivationService();
        var service = new ClientCycleService(
            registry,
            new DefaultCycleEligibilityPolicy(),
            activation);

        var result = await service.CycleAsync(
            currentClientId: null,
            CycleDirection.Next);

        Assert.Equal(ActivationStatus.NoEligibleClients, result.Status);
        Assert.Null(activation.LastActivatedWindow);
    }

    private static ClientSnapshot CreateClient(
        string name,
        ClientState state,
        long windowId) =>
        new(
            ClientId.New(),
            ClientSessionId.New(),
            new WindowId(windowId),
            name,
            state);

    private sealed class StubRegistry(IReadOnlyList<ClientSnapshot> clients)
        : IClientRegistry
    {
        public IReadOnlyList<ClientSnapshot> GetSnapshot() => clients;
    }

    private sealed class RecordingActivationService : IWindowActivationService
    {
        public WindowId? LastActivatedWindow { get; private set; }

        public Task<ActivationResult> ActivateAsync(
            WindowId windowId,
            CancellationToken cancellationToken = default)
        {
            LastActivatedWindow = windowId;
            return Task.FromResult(ActivationResult.Success());
        }
    }
}
