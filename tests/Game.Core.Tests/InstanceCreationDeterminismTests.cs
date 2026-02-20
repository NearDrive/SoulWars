using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR64")]
public sealed class InstanceCreationDeterminismTests
{
    [Fact]
    public void DeterministicInstanceIds_AndChecksum_AreStableAcrossRuns()
    {
        SimulationConfig config = SimulationConfig.Default(seed: 1337);

        (ZoneInstanceState firstA, ZoneInstanceState secondA, string checksumA) = BuildTwoInstances(config);
        (ZoneInstanceState firstB, ZoneInstanceState secondB, string checksumB) = BuildTwoInstances(config);

        Assert.Equal(firstA.Id, firstB.Id);
        Assert.Equal(secondA.Id, secondB.Id);
        Assert.Equal(firstA.Ordinal, firstB.Ordinal);
        Assert.Equal(secondA.Ordinal, secondB.Ordinal);
        Assert.Equal(checksumA, checksumB);
    }

    [Fact]
    public void SnapshotRoundtrip_PreservesOrdinalCounter_AndDeterministicNextId()
    {
        SimulationConfig config = SimulationConfig.Default(seed: 42);
        WorldState world = Simulation.CreateInitialState(config);

        InstanceRegistry registry = world.InstanceRegistryOrEmpty;
        (registry, ZoneInstanceState created) = registry.CreateInstance(config.Seed, new PartyId(10), new ZoneId(1), creationTick: world.Tick);
        world = world.WithInstanceRegistry(registry);

        byte[] snapshot = WorldStateSerializer.SaveToBytes(world);
        WorldState loaded = WorldStateSerializer.LoadFromBytes(snapshot);

        Assert.Equal(world.InstanceRegistryOrEmpty.NextInstanceOrdinal, loaded.InstanceRegistryOrEmpty.NextInstanceOrdinal);
        Assert.True(world.InstanceRegistryOrEmpty.Instances.SequenceEqual(loaded.InstanceRegistryOrEmpty.Instances));

        (InstanceRegistry nextRegistryA, ZoneInstanceState nextA) = world.InstanceRegistryOrEmpty.CreateInstance(config.Seed, new PartyId(11), new ZoneId(1), creationTick: world.Tick);
        (InstanceRegistry nextRegistryB, ZoneInstanceState nextB) = loaded.InstanceRegistryOrEmpty.CreateInstance(config.Seed, new PartyId(11), new ZoneId(1), creationTick: loaded.Tick);

        Assert.Equal(nextA.Id, nextB.Id);
        Assert.Equal(nextRegistryA.NextInstanceOrdinal, nextRegistryB.NextInstanceOrdinal);
        Assert.Equal(created.Ordinal + 1, nextA.Ordinal);
    }

    private static (ZoneInstanceState First, ZoneInstanceState Second, string Checksum) BuildTwoInstances(SimulationConfig config)
    {
        WorldState world = Simulation.CreateInitialState(config);
        InstanceRegistry registry = world.InstanceRegistryOrEmpty;

        (registry, ZoneInstanceState first) = registry.CreateInstance(config.Seed, new PartyId(501), new ZoneId(1), creationTick: 10);
        (registry, ZoneInstanceState second) = registry.CreateInstance(config.Seed, new PartyId(777), new ZoneId(1), creationTick: 10);

        world = world.WithInstanceRegistry(registry);
        return (first, second, StateChecksum.Compute(world));
    }
}
