using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "Persistence")]
public sealed class PartySnapshotRoundtripTests
{
    [Fact]
    public void SnapshotRoundtrip_PreservesPartyStateAndChecksum()
    {
        SimulationConfig config = CreateConfig(seed: 88);
        WorldState world = Simulation.CreateInitialState(config);
        world = world with
        {
            PartyRegistry = world.PartyRegistryOrEmpty.CreateParty(new EntityId(42)),
            PartyInviteRegistry = new PartyInviteRegistry(ImmutableArray.Create(new PartyInvite(new EntityId(77), new PartyId(1), new EntityId(42), world.Tick))).Canonicalize()
        };

        string beforeChecksum = StateChecksum.Compute(world);
        byte[] snapshot = WorldStateSerializer.SaveToBytes(world);

        WorldState reloaded = WorldStateSerializer.LoadFromBytes(snapshot);
        string afterChecksum = StateChecksum.Compute(reloaded);

        Assert.Equal(beforeChecksum, afterChecksum);

        PartyRegistry beforeRegistry = world.PartyRegistryOrEmpty.Canonicalize();
        PartyRegistry afterRegistry = reloaded.PartyRegistryOrEmpty.Canonicalize();
        Assert.Equal(beforeRegistry.NextPartySequence, afterRegistry.NextPartySequence);
        Assert.True(beforeRegistry.Parties.SequenceEqual(afterRegistry.Parties));
        Assert.Equal(world.PartyInviteRegistryOrEmpty.Canonicalize(), reloaded.PartyInviteRegistryOrEmpty.Canonicalize());
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
