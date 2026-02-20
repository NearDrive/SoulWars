using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class PartyReplayVerifyTests
{
    [Fact]
    [Trait("Category", "ReplayVerify")]
    [Trait("Category", "Canary")]
    public void ReplayVerify_PartyInviteAcceptLeave_IsDeterministic()
    {
        SimulationConfig config = CreateConfig(301);
        string checksumA = RunScenario(config);
        string checksumB = RunScenario(config);

        Assert.Equal(checksumA, checksumB);
    }

    private static string RunScenario(SimulationConfig config)
    {
        WorldState state = Simulation.CreateInitialState(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(3), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2))))));

        state = state with { PartyRegistry = state.PartyRegistryOrEmpty.CreateParty(new EntityId(1)) };

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.InviteToParty, new EntityId(1), new ZoneId(1), InviteePlayerId: new EntityId(2)))));

        PartyInvite invite = Assert.Single(state.PartyInviteRegistryOrEmpty.Invites);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.AcceptPartyInvite, new EntityId(2), new ZoneId(1), PartyId: invite.PartyId))));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.LeaveParty, new EntityId(1), new ZoneId(1)))));

        return StateChecksum.ComputeGlobalChecksum(state);
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
