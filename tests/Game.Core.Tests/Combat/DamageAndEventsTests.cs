using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class DamageAndEventsTests
{
    [Fact]
    public void Damage_IsDeterministic_NoRng()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnDuel(config);

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(cast)));

        ZoneState zone = Assert.Single(state.Zones);
        EntityState target = zone.Entities.Single(e => e.Id.Value == 2);

        Assert.Equal(88, target.Hp);
        CombatEvent evt = Assert.Single(state.CombatEvents);
        Assert.Equal(12, evt.Amount);
        Assert.Equal(CombatEventType.Damage, evt.Type);
    }

    [Fact]
    public void CombatEvents_AreCanonicalOrdered()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnThree(config);

        WorldCommand castFrom3 = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(3),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(4));

        WorldCommand castFrom1 = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(castFrom3, castFrom1)));

        Assert.Equal(2, state.CombatEvents.Length);
        Assert.Equal(1, state.CombatEvents[0].SourceId.Value);
        Assert.Equal(3, state.CombatEvents[1].SourceId.Value);
    }

    [Fact]
    public void Snapshot_IncludesCombatEvents_StableBytesOrHash()
    {
        SimulationConfig config = CreateConfig();

        byte[] runA = BuildSnapshotBytes(config);
        byte[] runB = BuildSnapshotBytes(config);

        Assert.Equal(Convert.ToHexString(runA), Convert.ToHexString(runB));
    }

    [Fact]
    public void ReplayVerify_CombatScenario_Passes()
    {
        SimulationConfig config = CreateConfig();
        string runA = RunScenarioChecksum(config);
        string runB = RunScenarioChecksum(config);

        Assert.Equal(runA, runB);
    }

    private static byte[] BuildSnapshotBytes(SimulationConfig config)
    {
        WorldState state = SpawnDuel(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(1), new ZoneId(1), SkillId: new SkillId(10), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(2)))));

        return WorldStateSerializer.SaveToBytes(state);
    }

    private static string RunScenarioChecksum(SimulationConfig config)
    {
        WorldState state = SpawnDuel(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: 1, MoveY: 0),
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(1), new ZoneId(1), SkillId: new SkillId(10), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(2)))));
        return StateChecksum.Compute(state);
    }

    private static WorldState SpawnDuel(SimulationConfig config)
    {
        TileMap map = BuildOpenMap(config.MapWidth, config.MapHeight);
        WorldState state = new(
            Tick: 0,
            Zones: ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            EntityLocations: ImmutableArray<EntityLocation>.Empty,
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            CombatEvents: ImmutableArray<CombatEvent>.Empty);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(2))))));

        ZoneState zone = Assert.Single(state.Zones);
        state = state.WithZoneUpdated(zone.WithEntities(zone.Entities
            .Select(e => e.Id.Value == 1
                ? e with { AttackDamage = 10, DefenseStats = new DefenseStatsComponent(Armor: 0, MagicResist: 0) }
                : e with { AttackDamage = 7, DefenseStats = new DefenseStatsComponent(Armor: 3, MagicResist: 0) })
            .ToImmutableArray()));
        return state;
    }

    private static WorldState SpawnThree(SimulationConfig config)
    {
        WorldState state = SpawnDuel(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(3), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(4), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(2))))));
        return state;
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

    private static SimulationConfig CreateConfig()
    {
        Fix32 range = Fix32.FromInt(6);
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
                new SkillDefinition(new SkillId(10), range.Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 1, ResourceCost: 0, TargetKind: CastTargetKind.Entity, EffectKind: SkillEffectKind.Damage, BaseAmount: 5, CoefRaw: Fix32.OneRaw)),
            Invariants: InvariantOptions.Enabled);
    }
}
