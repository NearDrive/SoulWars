using Game.Core;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "DoD")]
public sealed class PartyCreationDeterminismTests
{
    [Fact]
    public void TwoFreshSimulations_SamePartyActions_ProduceSamePartyIdAndChecksum()
    {
        SimulationConfig config = CreateConfig(seed: 77);

        (PartyState partyA, string checksumA) = CreateSinglePartyAndChecksum(config, leaderId: new EntityId(100));
        (PartyState partyB, string checksumB) = CreateSinglePartyAndChecksum(config, leaderId: new EntityId(100));

        Assert.Equal(partyA.Id, partyB.Id);
        Assert.Equal(partyA.LeaderId, partyB.LeaderId);
        Assert.True(partyA.Members.SequenceEqual(partyB.Members));
        Assert.Equal(checksumA, checksumB);
    }

    private static (PartyState Party, string Checksum) CreateSinglePartyAndChecksum(SimulationConfig config, EntityId leaderId)
    {
        WorldState world = Simulation.CreateInitialState(config);
        PartyRegistry updatedRegistry = world.PartyRegistryOrEmpty.CreateParty(leaderId);
        world = world with { PartyRegistry = updatedRegistry };

        PartyState party = Assert.Single(world.PartyRegistryOrEmpty.Parties);
        string checksum = StateChecksum.Compute(world);
        return (party, checksum);
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
