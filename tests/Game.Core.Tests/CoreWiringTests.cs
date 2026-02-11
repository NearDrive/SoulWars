using Game.Core;

namespace Game.Core.Tests;

public sealed class CoreWiringTests
{
    [Fact]
    public void CoreProject_IsReferenced()
    {
        CoreMarker marker = new();

        Assert.NotNull(marker);
    }
}
