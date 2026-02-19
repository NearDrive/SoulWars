using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class AoeTests
{
    [Fact]
    public void Aoe_SelectsTargetsWithinRadius_SortedByDistThenId()
    {
        SimulationConfig config = CreateConfig(maxTargets: 0);
        WorldState state = SpawnArena(config, BuildTargets(
            (new EntityId(2), 6, 5),
            (new EntityId(3), 8, 5),
            (new EntityId(4), 7, 5),
            (new EntityId(5), 6, 4),
            (new EntityId(6), 3, 3),
            (new EntityId(7), 11, 5)));

        state = CastAoe(state, config, caster: new EntityId(1), targetX: 6, targetY: 5);

        Assert.Equal(new[] { 2, 4, 5, 3 }, state.CombatEvents.Select(e => e.TargetId.Value).ToArray());
    }

    [Fact]
    public void Aoe_Respects_MaxTargets_BudgetDeterministic()
    {
        SimulationConfig config = CreateConfig(maxTargets: 8);
        List<(EntityId Id, int X, int Y)> targets = new();
        for (int i = 0; i < 20; i++)
        {
            int entityId = 2 + i;
            int ring = i / 4;
            targets.Add((new EntityId(entityId), 6 + ring, 6 + (i % 2)));
        }

        WorldState state = SpawnArena(config, targets);
        state = CastAoe(state, config, caster: new EntityId(1), targetX: 6, targetY: 6);

        Assert.Equal(8, state.CombatEvents.Length);

        int[] expected = state.Zones[0].Entities
            .Where(e => e.Id.Value != 1)
            .Select(e => new { e.Id, DistSq = (e.Pos - new Vec2Fix(Fix32.FromInt(6), Fix32.FromInt(6))).LengthSq() })
            .OrderBy(x => x.DistSq)
            .ThenBy(x => x.Id.Value)
            .Take(8)
            .Select(x => x.Id.Value)
            .ToArray();

        Assert.Equal(expected, state.CombatEvents.Select(e => e.TargetId.Value).ToArray());
    }

    [Fact]
    public void Aoe_CombatEvents_CanonicalOrder()
    {
        SimulationConfig config = CreateConfig(maxTargets: 0);
        WorldState state = SpawnArena(config, BuildTargets(
            (new EntityId(2), 6, 5),
            (new EntityId(3), 7, 5),
            (new EntityId(4), 10, 10),
            (new EntityId(5), 11, 10)));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(
                Kind: WorldCommandKind.CastSkill,
                EntityId: new EntityId(10),
                ZoneId: new ZoneId(1),
                SkillId: new SkillId(40),
                TargetKind: CastTargetKind.Point,
                TargetPosXRaw: Fix32.FromInt(10).Raw,
                TargetPosYRaw: Fix32.FromInt(10).Raw),
            new WorldCommand(
                Kind: WorldCommandKind.CastSkill,
                EntityId: new EntityId(1),
                ZoneId: new ZoneId(1),
                SkillId: new SkillId(40),
                TargetKind: CastTargetKind.Point,
                TargetPosXRaw: Fix32.FromInt(6).Raw,
                TargetPosYRaw: Fix32.FromInt(5).Raw))));

        Assert.Equal(new[] { 1, 1, 10, 10 }, state.CombatEvents.Select(e => e.SourceId.Value).ToArray());
        Assert.Equal(new[] { 2, 3, 4, 5 }, state.CombatEvents.Select(e => e.TargetId.Value).ToArray());
    }

    [Fact]
    public void ReplayVerify_AoeScenario_Passes()
    {
        SimulationConfig config = CreateConfig(maxTargets: 8);
        string runA = RunReplay(config);
        string runB = RunReplay(config);

        Assert.Equal(runA, runB);
    }

    private static string RunReplay(SimulationConfig config)
    {
        WorldState state = SpawnArena(config, BuildTargets(
            (new EntityId(2), 6, 5),
            (new EntityId(3), 7, 5),
            (new EntityId(4), 8, 5)));

        state = CastAoe(state, config, caster: new EntityId(1), targetX: 6, targetY: 5);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: 1, MoveY: 0),
            new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(2), new ZoneId(1), MoveX: -1, MoveY: 0))));

        return StateChecksum.ComputeGlobalChecksum(state);
    }

    private static WorldState CastAoe(WorldState state, SimulationConfig config, EntityId caster, int targetX, int targetY)
    {
        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(
                Kind: WorldCommandKind.CastSkill,
                EntityId: caster,
                ZoneId: new ZoneId(1),
                SkillId: new SkillId(40),
                TargetKind: CastTargetKind.Point,
                TargetPosXRaw: Fix32.FromInt(targetX).Raw,
                TargetPosYRaw: Fix32.FromInt(targetY).Raw))));
    }

    private static IEnumerable<(EntityId Id, int X, int Y)> BuildTargets(params (EntityId Id, int X, int Y)[] values) => values;

    private static WorldState SpawnArena(SimulationConfig config, IEnumerable<(EntityId Id, int X, int Y)> targets)
    {
        TileMap map = BuildOpenMap(config.MapWidth, config.MapHeight);
        WorldState state = new(
            Tick: 0,
            Zones: ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            EntityLocations: ImmutableArray<EntityLocation>.Empty,
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            CombatEvents: ImmutableArray<CombatEvent>.Empty);

        List<WorldCommand> commands = new()
        {
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(10), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(12), Fix32.FromInt(12)))
        };

        foreach ((EntityId id, int x, int y) in targets)
        {
            commands.Add(new WorldCommand(WorldCommandKind.EnterZone, id, new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(x), Fix32.FromInt(y))));
        }

        return Simulation.Step(config, state, new Inputs(commands.ToImmutableArray()));
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

    private static SimulationConfig CreateConfig(int maxTargets)
    {
        return new SimulationConfig(
            Seed: 303,
            TickHz: 20,
            DtFix: new Fix32(3277),
            MoveSpeed: Fix32.FromInt(4),
            MaxSpeed: Fix32.FromInt(4),
            Radius: new Fix32(16384),
            ZoneCount: 1,
            MapWidth: 20,
            MapHeight: 20,
            NpcCountPerZone: 0,
            NpcWanderPeriodTicks: 30,
            NpcAggroRange: Fix32.FromInt(6),
            SkillDefinitions: ImmutableArray.Create(
                new SkillDefinition(new SkillId(40), RangeQRaw: Fix32.FromInt(16).Raw, HitRadiusRaw: Fix32.FromInt(3).Raw, MaxTargets: maxTargets, CooldownTicks: 1, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, TargetKind: CastTargetKind.Point, EffectKind: SkillEffectKind.Damage, BaseAmount: 5, CoefRaw: Fix32.OneRaw)),
            Invariants: InvariantOptions.Enabled,
            MaxCombatEventsPerTick: 64);
    }
}
