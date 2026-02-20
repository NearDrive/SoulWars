using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "DoD")]
public sealed class PartyMembershipInvariantTests
{
    [Fact]
    public void LeaderLeave_ReassignsToSmallestMemberId()
    {
        SimulationConfig config = CreateConfig(201);
        WorldState state = SpawnPlayers(config, new EntityId(1), new EntityId(4), new EntityId(2));
        state = state with { PartyRegistry = state.PartyRegistryOrEmpty.CreateParty(new EntityId(1)) };

        state = InviteAndAccept(config, state, inviter: new EntityId(1), invitee: new EntityId(4));
        state = InviteAndAccept(config, state, inviter: new EntityId(1), invitee: new EntityId(2));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.LeaveParty, new EntityId(1), new ZoneId(1)))));

        PartyState party = Assert.Single(state.PartyRegistryOrEmpty.Parties);
        Assert.Equal(2, party.LeaderId.Value);
        Assert.Equal(new[] { 2, 4 }, party.Members.Select(m => m.EntityId.Value).ToArray());
    }

    [Fact]
    public void LastMemberLeave_DisbandsParty()
    {
        SimulationConfig config = CreateConfig(202);
        WorldState state = SpawnPlayers(config, new EntityId(8));
        state = state with { PartyRegistry = state.PartyRegistryOrEmpty.CreateParty(new EntityId(8)) };

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.LeaveParty, new EntityId(8), new ZoneId(1)))));

        Assert.Empty(state.PartyRegistryOrEmpty.Parties);
    }

    private static WorldState InviteAndAccept(SimulationConfig config, WorldState state, EntityId inviter, EntityId invitee)
    {
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.InviteToParty, inviter, new ZoneId(1), InviteePlayerId: invitee))));

        PartyInvite invite = Assert.Single(state.PartyInviteRegistryOrEmpty.Invites.Where(i => i.InviteeId.Value == invitee.Value));

        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.AcceptPartyInvite, invitee, new ZoneId(1), PartyId: invite.PartyId))));
    }

    private static WorldState SpawnPlayers(SimulationConfig config, params EntityId[] playerIds)
    {
        WorldState state = Simulation.CreateInitialState(config);
        ImmutableArray<WorldCommand> enters = playerIds
            .Select((id, index) => new WorldCommand(WorldCommandKind.EnterZone, id, new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(3 + index), Fix32.FromInt(3))))
            .ToImmutableArray();
        return Simulation.Step(config, state, new Inputs(enters));
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
