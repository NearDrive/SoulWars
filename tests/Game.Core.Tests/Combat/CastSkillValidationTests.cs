using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class CastSkillValidationTests
{
    [Fact]
    public void CastSkill_OutOfRange_Rejected_Fix32()
    {
        SimulationConfig config = CreateConfigWithSkill(range: Fix32.FromInt(4));
        WorldState state = SpawnDuel(config, new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)), new Vec2Fix(Fix32.FromInt(7), Fix32.FromInt(2)));

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone));
        CastResult result = Simulation.ValidateCastSkill(config, state.Tick + 1, zone, cast);

        Assert.Equal(CastResult.Rejected_OutOfRange, result);
    }

    [Fact]
    public void CastSkill_InRange_Accepted_Fix32()
    {
        SimulationConfig config = CreateConfigWithSkill(range: Fix32.FromInt(4));
        WorldState state = SpawnDuel(config, new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)), new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(2)));

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState beforeZone));
        CastResult result = Simulation.ValidateCastSkill(config, state.Tick + 1, beforeZone, cast);

        Assert.Equal(CastResult.Ok, result);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(cast)));
        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState afterZone));

        EntityState caster = afterZone.Entities.Single(e => e.Id.Value == 1);
        Assert.Equal(state.Tick, caster.LastAttackTick);
        Assert.Equal(6, caster.AttackCooldownTicks);
    }


    [Fact]
    public void CastSkill_WithRequiresLoS_Blocked_Rejected()
    {
        SimulationConfig config = CreateConfigWithSkill(range: Fix32.FromInt(6), flags: SkillFlags.RequiresLineOfSight);
        WorldState state = SpawnDuel(config, new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)), new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(2)), blockedTiles: [(3, 2)]);

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState beforeZone));
        EntityState beforeCaster = beforeZone.Entities.Single(e => e.Id.Value == 1);
        int beforeCombatEventCount = state.CombatEvents.Length;

        CastResult result = Simulation.ValidateCastSkill(config, state.Tick + 1, beforeZone, cast);
        Assert.Equal(CastResult.Rejected_InvalidTarget, result);

        WorldState next = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(cast)));
        Assert.Empty(next.SkillCastIntents);
        Assert.Equal(beforeCombatEventCount, next.CombatEvents.Length);

        Assert.True(next.TryGetZone(new ZoneId(1), out ZoneState afterZone));
        EntityState afterCaster = afterZone.Entities.Single(e => e.Id.Value == 1);
        Assert.Equal(beforeCaster.LastAttackTick, afterCaster.LastAttackTick);
        Assert.True(afterCaster.SkillCooldowns.IsReady(new SkillId(10)));
    }

    [Fact]
    public void CastSkill_OrderCanonical_SameTick()
    {
        SimulationConfig config = CreateConfigWithSkill(range: Fix32.FromInt(6));
        WorldState state = SpawnDuel(config, new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)), new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(2)));
        state = AddCaster(config, state, new EntityId(3), new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2)));

        WorldCommand castFrom3 = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(3),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(12),
            TargetKind: CastTargetKind.Self);

        WorldCommand castFrom1 = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(12),
            TargetKind: CastTargetKind.Self);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(castFrom3, castFrom1)));
        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone));

        EntityState caster1 = zone.Entities.Single(e => e.Id.Value == 1);
        EntityState caster3 = zone.Entities.Single(e => e.Id.Value == 3);
        Assert.Equal(state.Tick, caster1.LastAttackTick);
        Assert.Equal(state.Tick, caster3.LastAttackTick);
    }

    [Fact]
    public void Replay_CastSkill_NoDrift()
    {
        SimulationConfig config = CreateConfigWithSkill(range: Fix32.FromInt(5));

        string runA = RunCastSkillSequence(config);
        string runB = RunCastSkillSequence(config);

        Assert.Equal(runA, runB);
    }

    private static string RunCastSkillSequence(SimulationConfig config)
    {
        WorldState state = SpawnDuel(config, new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)), new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(2)));

        WorldCommand castAtEntity = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        WorldCommand castAtPoint = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(2),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(11),
            TargetKind: CastTargetKind.Point,
            TargetPosXRaw: Fix32.FromInt(2).Raw,
            TargetPosYRaw: Fix32.FromInt(1).Raw);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(castAtEntity, castAtPoint)));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: 1, MoveY: 0),
            new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(2), new ZoneId(1), MoveX: -1, MoveY: 0))));

        return StateChecksum.ComputeGlobalChecksum(state);
    }

    private static WorldState SpawnDuel(SimulationConfig config, Vec2Fix p1, Vec2Fix p2, params (int X, int Y)[] blockedTiles)
    {
        TileMap map = BuildOpenMap(config.MapWidth, config.MapHeight, blockedTiles);
        WorldState state = new(
            Tick: 0,
            Zones: ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            EntityLocations: ImmutableArray<EntityLocation>.Empty,
            LootEntities: ImmutableArray<LootEntityState>.Empty);

        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: p1),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: p2))));
    }

    private static TileMap BuildOpenMap(int width, int height, params (int X, int Y)[] blockedTiles)
    {
        HashSet<(int X, int Y)> blocked = blockedTiles.Length == 0 ? [] : [.. blockedTiles];
        ImmutableArray<TileKind>.Builder tiles = ImmutableArray.CreateBuilder<TileKind>(width * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool obstacle = blocked.Contains((x, y));
                tiles.Add(border || obstacle ? TileKind.Solid : TileKind.Empty);
            }
        }

        return new TileMap(width, height, tiles.MoveToImmutable());
    }

    private static WorldState AddCaster(SimulationConfig config, WorldState state, EntityId entityId, Vec2Fix pos)
    {
        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, entityId, new ZoneId(1), SpawnPos: pos))));
    }

    private static SimulationConfig CreateConfigWithSkill(Fix32 range, SkillFlags flags = SkillFlags.None)
    {
        return new SimulationConfig(
            Seed: 55,
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
                new SkillDefinition(new SkillId(10), range.Raw, HitRadiusRaw: Fix32.OneRaw, MaxTargets: 1, CooldownTicks: 6, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, TargetType: SkillTargetType.Entity, Flags: flags),
                new SkillDefinition(new SkillId(11), range.Raw, HitRadiusRaw: Fix32.FromInt(2).Raw, CooldownTicks: 4, ResourceCost: 0, TargetKind: CastTargetKind.Point),
                new SkillDefinition(new SkillId(12), range.Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 2, ResourceCost: 0, TargetKind: CastTargetKind.Self)),
            Invariants: InvariantOptions.Enabled);
    }
}
