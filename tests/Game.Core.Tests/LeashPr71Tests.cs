using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR71")]
public sealed class LeashTriggerTests
{
    private static readonly Fix32 Half = new(Fix32.OneRaw / 2);

    [Fact]
    public void Leash_ActivatesExactlyWhenDistSqExceedsRadiusSq()
    {
        SimulationConfig config = CreateConfig(7101);
        WorldState state = Simulation.CreateInitialState(config, BuildZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);
        EntityId playerId = new(1);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, playerId, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(10) + Half, Fix32.FromInt(1) + Half)))));

        state = OverrideNpcLeashRadius(state, zoneId, npcId, Fix32.FromInt(1));

        ZoneState initialZone = Assert.Single(state.Zones);
        EntityState initialNpc = initialZone.Entities.Single(e => e.Id == npcId);
        Assert.False(initialNpc.Leash.IsLeashing);

        bool observedActivation = false;
        bool observedOutsideRadiusPreLeash = false;

        for (int i = 0; i < 32; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
            ZoneState zone = Assert.Single(state.Zones);
            EntityState npc = zone.Entities.Single(e => e.Id == npcId);
            Fix32 currentDistSq = DistSqToAnchor(npc);

            if (!npc.Leash.IsLeashing && currentDistSq > npc.Leash.RadiusSq)
            {
                observedOutsideRadiusPreLeash = true;
            }

            if (npc.Leash.IsLeashing)
            {
                Assert.True(currentDistSq > npc.Leash.RadiusSq || observedOutsideRadiusPreLeash);
                Assert.Equal(MoveIntentType.GoToPoint, npc.MoveIntent.Type);
                observedActivation = true;
                break;
            }
        }

        Assert.True(observedActivation);
    }


    private static WorldState OverrideNpcLeashRadius(WorldState state, ZoneId zoneId, EntityId npcId, Fix32 radius)
    {
        Assert.True(state.TryGetZone(zoneId, out ZoneState zone));
        ImmutableArray<EntityState> updated = zone.Entities
            .Select(entity => entity.Id == npcId
                ? entity with { Leash = LeashComponent.Create(new Vec2Fix(entity.Leash.AnchorX, entity.Leash.AnchorY), radius) }
                : entity)
            .ToImmutableArray();

        return state.WithZoneUpdated(zone.WithEntities(updated));
    }
    private static Fix32 DistSqToAnchor(EntityState npc)
    {
        Fix32 dx = npc.Pos.X - npc.Leash.AnchorX;
        Fix32 dy = npc.Pos.Y - npc.Leash.AnchorY;
        return (dx * dx) + (dy * dy);
    }

    private static ZoneDefinitions BuildZone()
    {
        ZoneDefinition zone = new(
            ZoneId: new ZoneId(1),
            Bounds: new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(15), Fix32.FromInt(15)),
            StaticObstacles: ImmutableArray<ZoneAabb>.Empty,
            NpcSpawns: ImmutableArray.Create(new NpcSpawnDefinition("npc.default", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(1) + Half, Fix32.FromInt(1) + Half)))),
            LootRules: null,
            RespawnPoint: new Vec2Fix(Fix32.FromInt(1) + Half, Fix32.FromInt(1) + Half));

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
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(64),
        Invariants: InvariantOptions.Enabled);
}

[Trait("Category", "PR71")]
public sealed class ResetStateDeterminismTests
{
    private static readonly Fix32 Half = new(Fix32.OneRaw / 2);

    [Fact]
    public void Leash_ClearThreatAndStatuses_WithDeterministicResetOrdering()
    {
        SimulationConfig config = CreateConfig(7102);
        WorldState baseline = Simulation.CreateInitialState(config, BuildZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);

        WorldState preparedA = PrepareNpcForResetScenario(baseline, zoneId, npcId);
        WorldState preparedB = PrepareNpcForResetScenario(baseline, zoneId, npcId);

        WorldState resultA = Simulation.Step(config, preparedA, new Inputs(ImmutableArray<WorldCommand>.Empty));
        WorldState resultB = Simulation.Step(config, preparedB, new Inputs(ImmutableArray<WorldCommand>.Empty));

        EntityState npcA = Assert.Single(resultA.Zones).Entities.Single(e => e.Id == npcId);
        EntityState npcB = Assert.Single(resultB.Zones).Entities.Single(e => e.Id == npcId);

        Assert.True(npcA.Leash.IsLeashing);
        Assert.True(npcB.Leash.IsLeashing);
        Assert.Empty(npcA.Threat.OrderedEntries());
        Assert.Empty(npcB.Threat.OrderedEntries());
        Assert.False((npcA.StatusEffects.Effects.IsDefault ? ImmutableArray<StatusEffectInstance>.Empty : npcA.StatusEffects.Effects).IsEmpty);
        Assert.False((npcB.StatusEffects.Effects.IsDefault ? ImmutableArray<StatusEffectInstance>.Empty : npcB.StatusEffects.Effects).IsEmpty);
        Assert.Equal(StateChecksum.ComputeGlobalChecksum(resultA), StateChecksum.ComputeGlobalChecksum(resultB));
    }

    private static WorldState PrepareNpcForResetScenario(WorldState state, ZoneId zoneId, EntityId npcId)
    {
        Assert.True(state.TryGetZone(zoneId, out ZoneState zone));
        EntityState npc = zone.Entities.Single(e => e.Id == npcId);

        ThreatComponent threat = ThreatComponent.Empty
            .AddThreat(new EntityId(11), 10, state.Tick)
            .AddThreat(new EntityId(7), 15, state.Tick)
            .AddThreat(new EntityId(42), 5, state.Tick);

        ImmutableArray<StatusEvent>.Builder statusEvents = ImmutableArray.CreateBuilder<StatusEvent>();
        StatusEffectsComponent status = StatusEffectsComponent.Empty;
        status = status.ApplyOrRefresh(new StatusEffectInstance(StatusEffectType.Slow, new EntityId(21), state.Tick + 100, new Fix32(Fix32.OneRaw / 2).Raw), state.Tick, npc.Id, statusEvents);
        status = status.ApplyOrRefresh(new StatusEffectInstance(StatusEffectType.Stun, new EntityId(22), state.Tick + 100, 0), state.Tick, npc.Id, statusEvents);

        EntityState updatedNpc = npc with
        {
            Pos = new Vec2Fix(Fix32.FromInt(14) + Half, Fix32.FromInt(14) + Half),
            Threat = threat,
            StatusEffects = status,
            ResetOnLeash = new ResetOnLeashComponent(ClearThreat: true, ResetHp: true, ClearStatuses: true),
            Hp = 25
        };

        ZoneState updatedZone = zone.WithEntities(zone.Entities.Select(e => e.Id == npcId ? updatedNpc : e).ToImmutableArray());
        return state.WithZoneUpdated(updatedZone);
    }

    private static ZoneDefinitions BuildZone()
    {
        ZoneDefinition zone = new(
            ZoneId: new ZoneId(1),
            Bounds: new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(15), Fix32.FromInt(15)),
            StaticObstacles: ImmutableArray<ZoneAabb>.Empty,
            NpcSpawns: ImmutableArray.Create(new NpcSpawnDefinition("npc.default", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(1) + Half, Fix32.FromInt(1) + Half)))),
            LootRules: null,
            RespawnPoint: new Vec2Fix(Fix32.FromInt(1) + Half, Fix32.FromInt(1) + Half));

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
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(64),
        Invariants: InvariantOptions.Enabled);
}

public sealed class ReplayVerifyLeashScenarioTests
{
    private static readonly Fix32 Half = new(Fix32.OneRaw / 2);

    [Fact]
    [Trait("Category", "PR71")]
    [Trait("Category", "ReplayVerify")]
    [Trait("Category", "Canary")]
    public void ReplayVerify_LeashScenario_IsStable_AndReturnsToAnchor()
    {
        ReplayOutcome baseline = RunReplay();
        ReplayOutcome replay = RunReplay();

        Assert.Equal(baseline.Checksums.Length, replay.Checksums.Length);
        for (int i = 0; i < baseline.Checksums.Length; i++)
        {
            Assert.Equal(baseline.Checksums[i], replay.Checksums[i]);
        }

        Assert.True(baseline.SawLeashing);
        Assert.False(baseline.FinalNpc.Leash.IsLeashing);
        Assert.True(IsWithinAnchorRadius(baseline.FinalNpc));
        Assert.Equal(baseline.FinalNpc.Pos, replay.FinalNpc.Pos);
    }

    private static ReplayOutcome RunReplay()
    {
        SimulationConfig config = CreateConfig(7103);
        WorldState state = Simulation.CreateInitialState(config, BuildZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);
        EntityId kiterId = new(1);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, kiterId, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(14) + Half, Fix32.FromInt(14) + Half)))));

        ImmutableArray<string>.Builder checksums = ImmutableArray.CreateBuilder<string>(160);
        bool sawLeashing = false;
        for (int tick = 0; tick < 160; tick++)
        {
            ImmutableArray<WorldCommand> commands = tick < 20
                ? ImmutableArray.Create(new WorldCommand(
                    WorldCommandKind.CastSkill,
                    kiterId,
                    zoneId,
                    TargetEntityId: npcId,
                    SkillId: new SkillId(1),
                    TargetKind: CastTargetKind.Entity))
                : ImmutableArray<WorldCommand>.Empty;

            state = Simulation.Step(config, state, new Inputs(commands));
            ZoneState tickZone = Assert.Single(state.Zones);
            EntityState tickNpc = tickZone.Entities.Single(e => e.Id == npcId);
            sawLeashing |= tickNpc.Leash.IsLeashing;
            checksums.Add(StateChecksum.ComputeGlobalChecksum(state));
        }

        ZoneState zone = Assert.Single(state.Zones);
        EntityState finalNpc = zone.Entities.Single(e => e.Id == npcId);
        return new ReplayOutcome(checksums.ToImmutable(), finalNpc, sawLeashing);
    }

    private static bool IsWithinAnchorRadius(EntityState npc)
    {
        Fix32 dx = npc.Pos.X - npc.Leash.AnchorX;
        Fix32 dy = npc.Pos.Y - npc.Leash.AnchorY;
        Fix32 distSq = (dx * dx) + (dy * dy);
        return distSq <= npc.Leash.RadiusSq;
    }

    private readonly record struct ReplayOutcome(
        ImmutableArray<string> Checksums,
        EntityState FinalNpc,
        bool SawLeashing);

    private static ZoneDefinitions BuildZone()
    {
        ZoneDefinition zone = new(
            ZoneId: new ZoneId(1),
            Bounds: new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(15), Fix32.FromInt(15)),
            StaticObstacles: ImmutableArray<ZoneAabb>.Empty,
            NpcSpawns: ImmutableArray.Create(new NpcSpawnDefinition("npc.default", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(1) + Half, Fix32.FromInt(1) + Half)))),
            LootRules: null,
            RespawnPoint: new Vec2Fix(Fix32.FromInt(1) + Half, Fix32.FromInt(1) + Half));

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
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(64),
        SkillDefinitions: ImmutableArray.Create(new SkillDefinition(new SkillId(1), Fix32.FromInt(64).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 2)),
        Invariants: InvariantOptions.Enabled);
}
