using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class SkillEffectSystemTests
{
    [Fact]
    public void SkillEffect_EntityTarget_AppliesDamage()
    {
        SimulationConfig config = CreateConfig(baseDamage: 10);
        WorldState state = SpawnDuel(config, targetHp: 50);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(1), new ZoneId(1), SkillId: new SkillId(80), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(2)))));

        EntityState target = Assert.Single(state.Zones).Entities.Single(e => e.Id.Value == 2);
        Assert.Equal(40, target.Hp);

        CombatLogEvent log = Assert.Single(state.CombatLogEvents);
        Assert.Equal(state.Tick, log.Tick);
        Assert.Equal(1, log.SourceId.Value);
        Assert.Equal(2, log.TargetId.Value);
        Assert.Equal(80, log.SkillId.Value);
        Assert.Equal(10, log.Amount);
        Assert.Equal(CombatLogKind.Damage, log.Kind);
    }

    [Fact]
    public void SkillEffect_DoesNotGoBelowZero()
    {
        SimulationConfig config = CreateConfig(baseDamage: 10);
        WorldState state = SpawnDuel(config, targetHp: 4);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(1), new ZoneId(1), SkillId: new SkillId(80), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(2)))));

        ZoneState zone = Assert.Single(state.Zones);
        EntityState target = zone.Entities.Single(e => e.Id.Value == 2);
        Assert.True(target.IsAlive);
        Assert.Equal(target.MaxHp, target.Hp);
        Assert.Contains(state.CombatLogEvents, e => e.Kind == CombatLogKind.Damage && e.TargetId.Value == 2 && e.Amount == 4);
        Assert.Contains(state.CombatLogEvents, e => e.Kind == CombatLogKind.Kill && e.TargetId.Value == 2);
    }

    [Fact]
    public void SkillEffect_Kill_EmitsKillEventOrMarksDead()
    {
        SimulationConfig config = CreateConfig(baseDamage: 10);
        WorldState state = SpawnDuel(config, targetHp: 10);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(1), new ZoneId(1), SkillId: new SkillId(80), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(2)))));

        ZoneState zone = Assert.Single(state.Zones);
        EntityState target = zone.Entities.Single(e => e.Id.Value == 2);
        Assert.True(target.IsAlive);
        Assert.Equal(target.MaxHp, target.Hp);
        Assert.Contains(state.CombatLogEvents, e => e.Kind == CombatLogKind.Kill && e.TargetId.Value == 2);
    }

    [Fact]
    public void SkillEffect_InvalidTarget_NoStateChange()
    {
        SimulationConfig config = CreateConfig(baseDamage: 10);
        WorldState state = SpawnDuel(config, targetHp: 50);
        int beforeHp = Assert.Single(state.Zones).Entities.Single(e => e.Id.Value == 2).Hp;

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(1), new ZoneId(1), SkillId: new SkillId(80), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(999)))));

        EntityState target = Assert.Single(state.Zones).Entities.Single(e => e.Id.Value == 2);
        Assert.Equal(beforeHp, target.Hp);
        Assert.Empty(state.CombatLogEvents);
    }

    [Fact]
    public void SkillEffect_OrderDeterministic_MultipleIntentsSameTick()
    {
        SimulationConfig config = CreateConfig(baseDamage: 5);
        WorldState state = SpawnTwoDuels(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(3), new ZoneId(1), SkillId: new SkillId(80), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(4)),
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(1), new ZoneId(1), SkillId: new SkillId(80), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(2)))));

        Assert.Equal(2, state.CombatLogEvents.Length);
        Assert.Equal(1, state.CombatLogEvents[0].SourceId.Value);
        Assert.Equal(3, state.CombatLogEvents[1].SourceId.Value);

        ZoneState zone = Assert.Single(state.Zones);
        Assert.Equal(15, zone.Entities.Single(e => e.Id.Value == 2).Hp);
        Assert.Equal(15, zone.Entities.Single(e => e.Id.Value == 4).Hp);
    }

    private static WorldState SpawnDuel(SimulationConfig config, int targetHp)
    {
        WorldState state = CreateBaseState();
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(2))))));

        ZoneState zone = Assert.Single(state.Zones);
        state = state.WithZoneUpdated(zone.WithEntities(zone.Entities.Select(e => e.Id.Value == 2 ? e with { Hp = targetHp, MaxHp = targetHp } : e).ToImmutableArray()));
        return state;
    }

    private static WorldState SpawnTwoDuels(SimulationConfig config)
    {
        WorldState state = SpawnDuel(config, targetHp: 20);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(3), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(4), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(2))))));

        ZoneState zone = Assert.Single(state.Zones);
        state = state.WithZoneUpdated(zone.WithEntities(zone.Entities.Select(e => e.Id.Value == 4 ? e with { Hp = 20, MaxHp = 20 } : e).ToImmutableArray()));
        return state;
    }

    private static WorldState CreateBaseState()
    {
        TileMap map = BuildOpenMap(16, 16);
        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            EntityLocations: ImmutableArray<EntityLocation>.Empty,
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            CombatEvents: ImmutableArray<CombatEvent>.Empty,
            CombatLogEvents: ImmutableArray<CombatLogEvent>.Empty);
    }

    private static TileMap BuildOpenMap(int width, int height)
    {
        ImmutableArray<TileKind>.Builder tiles = ImmutableArray.CreateBuilder<TileKind>(width * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                tiles.Add(border ? TileKind.Solid : TileKind.Empty);
            }
        }

        return new TileMap(width, height, tiles.MoveToImmutable());
    }

    private static SimulationConfig CreateConfig(int baseDamage)
    {
        return new SimulationConfig(
            Seed: 101,
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
            NpcAggroRange: Fix32.FromInt(6),
            SkillDefinitions: ImmutableArray.Create(
                new SkillDefinition(new SkillId(80), Fix32.FromInt(6).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 1, ResourceCost: 0, TargetKind: CastTargetKind.Entity, BaseDamage: baseDamage, DamageType: DamageType.Physical)),
            Invariants: InvariantOptions.Enabled);
    }
}
