using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "DoD")]
public sealed class PartyCommandValidationTests
{
    [Fact]
    public void Invite_WithoutExistingParty_DoesNotCreatePartyOrInvite()
    {
        SimulationConfig config = CreateConfig(101);
        WorldState state = SpawnPlayers(config, new EntityId(1), new EntityId(2));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.InviteToParty, new EntityId(1), new ZoneId(1), InviteePlayerId: new EntityId(2)))));

        Assert.Empty(state.PartyRegistryOrEmpty.Parties);
        Assert.Empty(state.PartyInviteRegistryOrEmpty.Invites);
    }

    [Fact]
    public void Invite_AndAccept_InvalidPartyId_NoStateChange()
    {
        SimulationConfig config = CreateConfig(102);
        WorldState state = SpawnPlayers(config, new EntityId(1), new EntityId(2));
        state = state with { PartyRegistry = state.PartyRegistryOrEmpty.CreateParty(new EntityId(1)) };

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.InviteToParty, new EntityId(1), new ZoneId(1), InviteePlayerId: new EntityId(2)))));

        PartyInvite invite = Assert.Single(state.PartyInviteRegistryOrEmpty.Invites);
        Assert.Equal(2, invite.InviteeId.Value);

        WorldState afterInvalidAccept = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.AcceptPartyInvite, new EntityId(2), new ZoneId(1), PartyId: new PartyId(999)))));

        Assert.Equal(state.PartyRegistryOrEmpty.Canonicalize(), afterInvalidAccept.PartyRegistryOrEmpty.Canonicalize());
        Assert.Equal(state.PartyInviteRegistryOrEmpty.Canonicalize(), afterInvalidAccept.PartyInviteRegistryOrEmpty.Canonicalize());
    }

    [Fact]
    public void MultipleInvitesSameTick_ConflictResolution_IsStable()
    {
        SimulationConfig config = CreateConfig(103);
        WorldState state = SpawnPlayers(config, new EntityId(10), new EntityId(20), new EntityId(30));

        PartyRegistry registry = state.PartyRegistryOrEmpty
            .CreateParty(new EntityId(10))
            .CreateParty(new EntityId(20));
        state = state with { PartyRegistry = registry };

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.InviteToParty, new EntityId(20), new ZoneId(1), InviteePlayerId: new EntityId(30)),
            new WorldCommand(WorldCommandKind.InviteToParty, new EntityId(10), new ZoneId(1), InviteePlayerId: new EntityId(30)))));

        PartyInvite resolved = Assert.Single(state.PartyInviteRegistryOrEmpty.Invites);
        Assert.Equal(30, resolved.InviteeId.Value);
        Assert.Equal(20, resolved.InviterId.Value);
    }

    private static WorldState SpawnPlayers(SimulationConfig config, params EntityId[] playerIds)
    {
        WorldState state = Simulation.CreateInitialState(config);
        ImmutableArray<WorldCommand> enters = playerIds
            .Select((id, index) => new WorldCommand(
                WorldCommandKind.EnterZone,
                id,
                new ZoneId(1),
                SpawnPos: new Vec2Fix(Fix32.FromInt(2 + index), Fix32.FromInt(2))))
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
