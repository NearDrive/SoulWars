using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class ServerWiringTests
{
    [Fact]
    public void ServerProject_IsReferenced()
    {
        ServerMarker marker = new();

        Assert.NotNull(marker);
    }
}
