using EveCommandCenter.Application.Abstractions;
using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Application.Cycling;

public sealed class DefaultCycleEligibilityPolicy : ICycleEligibilityPolicy
{
    public bool IsEligible(ClientSnapshot client) =>
        client.State is ClientState.Ready or ClientState.Minimized
        && !client.WindowId.IsEmpty;
}
