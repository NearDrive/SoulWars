using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class AggroRetargetCoreTests
{
    [Fact]
    public void Boss_DoesNotRetarget_DuringCooldown_EvenWhenAnotherPlayerLeadsThreat()
    {
        SimulationConfig config = CreateConfig(7410);
        WorldState state = Simulation.CreateInitialState(config, BuildZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);
        EntityId tankId = new(10);
        EntityId dpsId = new(11);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, tankId, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(3))),
            new WorldCommand(WorldCommandKind.EnterZone, dpsId, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(3))))));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, tankId, zoneId, TargetEntityId: npcId, SkillId: new SkillId(1), TargetKind: CastTargetKind.Entity))));

        state = ForceNpcRetargetCooldown(state, npcId, currentTarget: tankId, futureTick: state.Tick + 100);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, dpsId, zoneId, TargetEntityId: npcId, SkillId: new SkillId(2), TargetKind: CastTargetKind.Entity))));

        EntityState npc = GetNpc(state, npcId);
        Assert.Equal(MoveIntentType.ChaseEntity, npc.MoveIntent.Type);
        Assert.Equal(tankId.Value, npc.MoveIntent.TargetEntityId.Value);
    }

    [Fact]
    public void Boss_HoldsIntent_WhenPathBudgetIsZero_AndNoPathExists()
    {
        SimulationConfig config = CreateConfig(7411) with { AiBudgets = new AiBudgetConfig(0, 8, 32) };
        WorldState state = Simulation.CreateInitialState(config, BuildZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);
        EntityId tankId = new(10);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, tankId, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(3))))));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, tankId, zoneId, TargetEntityId: npcId, SkillId: new SkillId(1), TargetKind: CastTargetKind.Entity))));

        EntityState npc = GetNpc(state, npcId);
        Assert.Equal(MoveIntentType.Hold, npc.MoveIntent.Type);
        Assert.Equal(0, npc.MoveIntent.PathLen);
    }

    private static WorldState ForceNpcRetargetCooldown(WorldState state, EntityId npcId, EntityId currentTarget, int futureTick)
    {
        ZoneState zone = Assert.Single(state.Zones);
        EntityState npc = zone.Entities.Single(e => e.Id == npcId);
        EntityState updatedNpc = npc with
        {
            MoveIntent = npc.MoveIntent with
            {
                Type = MoveIntentType.ChaseEntity,
                TargetEntityId = currentTarget,
                NextAllowedRetargetTick = futureTick
            }
        };

        ZoneState updatedZone = zone.WithEntities(zone.Entities.Select(e => e.Id == npcId ? updatedNpc : e).ToImmutableArray());
        return state.WithZoneUpdated(updatedZone);
    }

    private static EntityState GetNpc(WorldState state, EntityId npcId)
        => Assert.Single(state.Zones[0].Entities.Where(e => e.Id == npcId));

    private static ZoneDefinitions BuildZone()
    {
        ZoneDefinition zone = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(16), Fix32.FromInt(16)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("npc.default", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(6), Fix32.FromInt(6))))),
            null,
            null,
            ImmutableArray<EncounterDefinition>.Empty);

        return new ZoneDefinitions(ImmutableArray.Create(zone));
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 16,
        MapHeight: 16,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 9999,
        NpcAggroRange: Fix32.FromInt(32),
        SkillDefinitions: ImmutableArray.Create(
            new SkillDefinition(new SkillId(1), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 2),
            new SkillDefinition(new SkillId(2), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 12)),
        Invariants: InvariantOptions.Enabled);
}
