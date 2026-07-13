using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Core.Tests;

public sealed class ClientIdTests
{
    [Fact]
    public void New_creates_non_empty_identifier()
    {
        var clientId = ClientId.New();

        Assert.False(clientId.IsEmpty);
    }
}
