using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class PointTargetingTests
{
    [Fact]
    public void PointTarget_SelectsNearest_TieBreakByEntityId()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnArena(config);

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(20),
            TargetKind: CastTargetKind.Point,
            TargetPosXRaw: Fix32.FromInt(6).Raw,
            TargetPosYRaw: Fix32.FromInt(6).Raw);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(cast)));

        CombatEvent evt = Assert.Single(state.CombatEvents);
        Assert.Equal(2, evt.TargetId.Value);
    }

    [Fact]
    public void PointTarget_NoCandidateInRadius_Rejected()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnArena(config);

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(20),
            TargetKind: CastTargetKind.Point,
            TargetPosXRaw: Fix32.FromInt(8).Raw,
            TargetPosYRaw: Fix32.FromInt(2).Raw);

        ZoneState zone = Assert.Single(state.Zones);
        CastResult result = Simulation.ValidateCastSkill(config, state.Tick + 1, zone, cast);

        Assert.Equal(CastResult.Rejected_InvalidTarget, result);
    }

    [Fact]
    public void PointTarget_CombatEvent_StableAcrossRuns()
    {
        SimulationConfig config = CreateConfig();
        string runA = RunPointScenario(config);
        string runB = RunPointScenario(config);

        Assert.Equal(runA, runB);
    }

    [Fact]
    public void ReplayVerify_SkillshotScenario_Passes()
    {
        SimulationConfig config = CreateConfig();
        string checksumA = RunPointReplayChecksum(config);
        string checksumB = RunPointReplayChecksum(config);

        Assert.Equal(checksumA, checksumB);
    }

    private static string RunPointScenario(SimulationConfig config)
    {
        WorldState state = SpawnArena(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(
                Kind: WorldCommandKind.CastSkill,
                EntityId: new EntityId(1),
                ZoneId: new ZoneId(1),
                SkillId: new SkillId(20),
                TargetKind: CastTargetKind.Point,
                TargetPosXRaw: Fix32.FromInt(6).Raw,
                TargetPosYRaw: Fix32.FromInt(6).Raw))));

        CombatEvent evt = Assert.Single(state.CombatEvents);
        return $"{evt.Tick}|{evt.SourceId.Value}|{evt.TargetId.Value}|{evt.SkillId.Value}|{(byte)evt.Type}|{evt.Amount}";
    }

    private static string RunPointReplayChecksum(SimulationConfig config)
    {
        WorldState state = SpawnArena(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(
                Kind: WorldCommandKind.CastSkill,
                EntityId: new EntityId(1),
                ZoneId: new ZoneId(1),
                SkillId: new SkillId(20),
                TargetKind: CastTargetKind.Point,
                TargetPosXRaw: Fix32.FromInt(6).Raw,
                TargetPosYRaw: Fix32.FromInt(6).Raw))));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: 1, MoveY: 0),
            new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(2), new ZoneId(1), MoveX: -1, MoveY: 0),
            new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(3), new ZoneId(1), MoveX: 0, MoveY: -1))));

        return StateChecksum.ComputeGlobalChecksum(state);
    }

    private static WorldState SpawnArena(SimulationConfig config)
    {
        TileMap map = BuildOpenMap(config.MapWidth, config.MapHeight);
        WorldState state = new(
            Tick: 0,
            Zones: ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            EntityLocations: ImmutableArray<EntityLocation>.Empty,
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            CombatEvents: ImmutableArray<CombatEvent>.Empty);

        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(6))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(3), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(7), Fix32.FromInt(6))))));
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
        Fix32 range = Fix32.FromInt(8);
        return new SimulationConfig(
            Seed: 202,
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
                new SkillDefinition(new SkillId(20), range.Raw, HitRadiusRaw: Fix32.FromInt(2).Raw, CooldownTicks: 1, ResourceCost: 0, TargetKind: CastTargetKind.Point, EffectKind: SkillEffectKind.Damage, BaseAmount: 5, CoefRaw: Fix32.OneRaw)),
            Invariants: InvariantOptions.Enabled);
    }
}
