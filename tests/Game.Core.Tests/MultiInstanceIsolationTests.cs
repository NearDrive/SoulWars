using Game.Core;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR64")]
public sealed class MultiInstanceIsolationTests
{
    [Fact]
    public void InstanceRng_IsDeterministicPerInstance_AndIsolatedAcrossInstances()
    {
        int serverSeed = 8080;
        int creationTick = 22;

        InstanceRegistry registry = InstanceRegistry.Empty;
        (registry, ZoneInstanceState instanceA) = registry.CreateInstance(serverSeed, new PartyId(1), new ZoneId(1), creationTick);
        (registry, ZoneInstanceState instanceB) = registry.CreateInstance(serverSeed, new PartyId(2), new ZoneId(1), creationTick);

        SimRng rngA1 = instanceA.CreateSimRng();
        SimRng rngA2 = instanceA.CreateSimRng();
        SimRng rngB = instanceB.CreateSimRng();

        int a1First = rngA1.NextInt(0, 1_000_000);
        int a1Second = rngA1.NextInt(0, 1_000_000);

        int a2First = rngA2.NextInt(0, 1_000_000);
        int a2Second = rngA2.NextInt(0, 1_000_000);

        int bFirst = rngB.NextInt(0, 1_000_000);
        int bSecond = rngB.NextInt(0, 1_000_000);

        Assert.Equal(a1First, a2First);
        Assert.Equal(a1Second, a2Second);

        Assert.True(a1First != bFirst || a1Second != bSecond,
            "Different instances must not share the same deterministic RNG stream.");
    }
}
