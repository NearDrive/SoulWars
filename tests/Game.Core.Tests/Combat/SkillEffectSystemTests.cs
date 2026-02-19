using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class SkillEffectSystemTests
{
    [Fact]
    public void Damage_Physical_ArmorReduces()
    {
        WorldState state = RunSingleHit(baseDamage: 10, damageType: DamageType.Physical, armor: 3, mr: 0, targetHp: 50);

        EntityState target = Assert.Single(state.Zones).Entities.Single(e => e.Id.Value == 2);
        Assert.Equal(43, target.Hp);

        CombatLogEvent log = Assert.Single(state.CombatLogEvents);
        Assert.Equal(10, log.RawAmount);
        Assert.Equal(7, log.FinalAmount);
    }

    [Fact]
    public void Damage_Magical_MRReduces()
    {
        WorldState state = RunSingleHit(baseDamage: 10, damageType: DamageType.Magical, armor: 0, mr: 4, targetHp: 50);

        EntityState target = Assert.Single(state.Zones).Entities.Single(e => e.Id.Value == 2);
        Assert.Equal(44, target.Hp);

        CombatLogEvent log = Assert.Single(state.CombatLogEvents);
        Assert.Equal(10, log.RawAmount);
        Assert.Equal(6, log.FinalAmount);
    }

    [Fact]
    public void Damage_True_IgnoresDefense()
    {
        WorldState state = RunSingleHit(baseDamage: 10, damageType: DamageType.True, armor: 999, mr: 999, targetHp: 50);

        EntityState target = Assert.Single(state.Zones).Entities.Single(e => e.Id.Value == 2);
        Assert.Equal(40, target.Hp);

        CombatLogEvent log = Assert.Single(state.CombatLogEvents);
        Assert.Equal(10, log.RawAmount);
        Assert.Equal(10, log.FinalAmount);
    }

    [Fact]
    public void Damage_NeverNegative()
    {
        WorldState state = RunSingleHit(baseDamage: 5, damageType: DamageType.Physical, armor: 10, mr: 0, targetHp: 50);

        EntityState target = Assert.Single(state.Zones).Entities.Single(e => e.Id.Value == 2);
        Assert.Equal(50, target.Hp);

        CombatLogEvent log = Assert.Single(state.CombatLogEvents);
        Assert.Equal(5, log.RawAmount);
        Assert.Equal(0, log.FinalAmount);
    }

    [Fact]
    public void CombatLog_ContainsRawAndFinal()
    {
        WorldState state = RunSingleHit(baseDamage: 12, damageType: DamageType.Physical, armor: 5, mr: 0, targetHp: 50);

        CombatLogEvent log = Assert.Single(state.CombatLogEvents);
        Assert.Equal(state.Tick, log.Tick);
        Assert.Equal(1, log.SourceId.Value);
        Assert.Equal(2, log.TargetId.Value);
        Assert.Equal(80, log.SkillId.Value);
        Assert.Equal(12, log.RawAmount);
        Assert.Equal(7, log.FinalAmount);
        Assert.Equal(CombatLogKind.Damage, log.Kind);
    }

    [Fact]
    public void OrderDeterministic_MultipleIntentsSameTick_WithDefense()
    {
        SimulationConfig config = CreateConfig(baseDamage: 10, damageType: DamageType.Physical);
        WorldState state = SpawnTwoDuels(config, armorEntity2: 3, armorEntity4: 6, mrEntity2: 0, mrEntity4: 0);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(3), new ZoneId(1), SkillId: new SkillId(80), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(4)),
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(1), new ZoneId(1), SkillId: new SkillId(80), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(2)))));

        Assert.Equal(2, state.CombatLogEvents.Length);
        Assert.Equal(1, state.CombatLogEvents[0].SourceId.Value);
        Assert.Equal(3, state.CombatLogEvents[1].SourceId.Value);
        Assert.Equal((10, 7), (state.CombatLogEvents[0].RawAmount, state.CombatLogEvents[0].FinalAmount));
        Assert.Equal((10, 4), (state.CombatLogEvents[1].RawAmount, state.CombatLogEvents[1].FinalAmount));

        ZoneState zone = Assert.Single(state.Zones);
        Assert.Equal(13, zone.Entities.Single(e => e.Id.Value == 2).Hp);
        Assert.Equal(16, zone.Entities.Single(e => e.Id.Value == 4).Hp);
    }

    private static WorldState RunSingleHit(int baseDamage, DamageType damageType, int armor, int mr, int targetHp)
    {
        SimulationConfig config = CreateConfig(baseDamage, damageType);
        WorldState state = SpawnDuel(config, targetHp, armor, mr);

        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(1), new ZoneId(1), SkillId: new SkillId(80), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(2)))));
    }

    private static WorldState SpawnDuel(SimulationConfig config, int targetHp, int armor, int mr)
    {
        WorldState state = CreateBaseState();
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(2))))));

        ZoneState zone = Assert.Single(state.Zones);
        state = state.WithZoneUpdated(zone.WithEntities(zone.Entities.Select(e => e.Id.Value == 2
            ? e with { Hp = targetHp, MaxHp = targetHp, DefenseStats = new DefenseStatsComponent(armor, mr) }
            : e).ToImmutableArray()));
        return state;
    }

    private static WorldState SpawnTwoDuels(SimulationConfig config, int armorEntity2, int armorEntity4, int mrEntity2, int mrEntity4)
    {
        WorldState state = SpawnDuel(config, targetHp: 20, armor: armorEntity2, mr: mrEntity2);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(3), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(4), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(2))))));

        ZoneState zone = Assert.Single(state.Zones);
        state = state.WithZoneUpdated(zone.WithEntities(zone.Entities.Select(e => e.Id.Value == 4
            ? e with { Hp = 20, MaxHp = 20, DefenseStats = new DefenseStatsComponent(armorEntity4, mrEntity4) }
            : e).ToImmutableArray()));
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

    private static SimulationConfig CreateConfig(int baseDamage, DamageType damageType)
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
                new SkillDefinition(new SkillId(80), Fix32.FromInt(6).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 1, ResourceCost: 0, TargetKind: CastTargetKind.Entity, BaseDamage: baseDamage, DamageType: damageType)),
            Invariants: InvariantOptions.Enabled);
    }
}
