using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR65")]
public sealed class InstanceSnapshotRoundtripTests
{
    [Fact]
    public void SnapshotRoundtrip_PreservesInstanceIdentitySeedAndChecksum()
    {
        SimulationConfig config = CreateConfig(seed: 6501);
        WorldState world = Simulation.CreateInitialState(config);

        PartyRegistry parties = world.PartyRegistryOrEmpty.CreateParty(new EntityId(100));
        PartyId partyId = Assert.Single(parties.Parties).Id;

        (InstanceRegistry registry, ZoneInstanceState created) = world.InstanceRegistryOrEmpty.CreateInstance(
            serverSeed: config.Seed,
            partyId: partyId,
            zoneId: new ZoneId(1),
            creationTick: world.Tick);

        world = world with { PartyRegistry = parties, InstanceRegistry = registry };

        for (int i = 0; i < 20; i++)
        {
            world = Simulation.Step(config, world, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        string beforeChecksum = StateChecksum.ComputeGlobalChecksum(world);
        byte[] snapshot = WorldStateSerializer.SaveToBytes(world);
        WorldState loaded = WorldStateSerializer.LoadFromBytes(snapshot);
        string afterChecksum = StateChecksum.ComputeGlobalChecksum(loaded);

        ZoneInstanceState loadedInstance = Assert.Single(loaded.InstanceRegistryOrEmpty.Instances);
        Assert.Equal(created.Id, loadedInstance.Id);
        Assert.Equal(created.RngSeed, loadedInstance.RngSeed);
        Assert.Equal(beforeChecksum, afterChecksum);
        Assert.Equal(world.InstanceRegistryOrEmpty.NextInstanceOrdinal, loaded.InstanceRegistryOrEmpty.NextInstanceOrdinal);
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: 1,
        MapWidth: 16,
        MapHeight: 16,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
