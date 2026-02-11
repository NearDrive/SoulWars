using Game.Core;
using Xunit;

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
