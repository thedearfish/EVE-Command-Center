using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Application.Abstractions;

public interface IClientRegistry
{
    IReadOnlyList<ClientSnapshot> GetSnapshot();
}
