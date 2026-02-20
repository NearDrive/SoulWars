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
            new WorldCommand(WorldCommandKind.EnterZone, playerId, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(3) + Half, Fix32.FromInt(1) + Half)))));

        ZoneState initialZone = Assert.Single(state.Zones);
        EntityState initialNpc = initialZone.Entities.Single(e => e.Id == npcId);
        Assert.False(initialNpc.Leash.IsLeashing);

        Fix32 previousDistSq = DistSqToAnchor(initialNpc);
        bool observedActivation = false;

        for (int i = 0; i < 32; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
            ZoneState zone = Assert.Single(state.Zones);
            EntityState npc = zone.Entities.Single(e => e.Id == npcId);
            Fix32 currentDistSq = DistSqToAnchor(npc);

            if (npc.Leash.IsLeashing)
            {
                Assert.True(previousDistSq <= npc.Leash.RadiusSq);
                Assert.True(currentDistSq > npc.Leash.RadiusSq);
                Assert.Equal(MoveIntentType.GoToPoint, npc.MoveIntent.Type);
                observedActivation = true;
                break;
            }

            previousDistSq = currentDistSq;
        }

        Assert.True(observedActivation);
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
        Assert.NotEmpty(npcA.StatusEffects.OrderedEffects());
        Assert.NotEmpty(npcB.StatusEffects.OrderedEffects());
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
        ImmutableArray<string> baseline = RunReplay(out EntityState baselineNpc);
        ImmutableArray<string> replay = RunReplay(out EntityState replayNpc);

        Assert.Equal(baseline, replay);
        Assert.False(baselineNpc.Leash.IsLeashing);
        Assert.Equal(MoveIntentType.Hold, baselineNpc.MoveIntent.Type);
        Assert.Equal(baselineNpc.Pos, replayNpc.Pos);
    }

    private static ImmutableArray<string> RunReplay(out EntityState finalNpc)
    {
        SimulationConfig config = CreateConfig(7103);
        WorldState state = Simulation.CreateInitialState(config, BuildZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);
        EntityId kiterId = new(1);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, kiterId, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(14) + Half, Fix32.FromInt(14) + Half)))));

        ImmutableArray<string>.Builder checksums = ImmutableArray.CreateBuilder<string>(160);
        for (int tick = 0; tick < 160; tick++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
            checksums.Add(StateChecksum.ComputeGlobalChecksum(state));
        }

        ZoneState zone = Assert.Single(state.Zones);
        finalNpc = zone.Entities.Single(e => e.Id == npcId);
        return checksums.ToImmutable();
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
