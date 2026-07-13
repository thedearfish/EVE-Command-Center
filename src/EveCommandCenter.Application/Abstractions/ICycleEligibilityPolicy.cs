using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Application.Abstractions;

public interface ICycleEligibilityPolicy
{
    bool IsEligible(ClientSnapshot client);
}
