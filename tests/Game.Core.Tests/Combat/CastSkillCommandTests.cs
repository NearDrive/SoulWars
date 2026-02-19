using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class CastSkillCommandTests
{
    [Fact]
    public void CastSkill_Ready_AcceptsAndStartsCooldown()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnDuel(config, new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)), new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2)));

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(cast)));

        Assert.Single(state.SkillCastIntents);
        SkillCastIntent intent = state.SkillCastIntents[0];
        Assert.Equal(state.Tick, intent.Tick);
        Assert.Equal(new EntityId(1), intent.CasterId);
        Assert.Equal(new SkillId(10), intent.SkillId);
        Assert.Equal(SkillTargetType.Entity, intent.TargetType);
        Assert.Equal(new EntityId(2), intent.TargetEntityId);

        ZoneState zone = Assert.Single(state.Zones);
        EntityState caster = Assert.Single(zone.Entities.Where(e => e.Id.Value == 1));
        Assert.False(caster.SkillCooldowns.IsReady(new SkillId(10)));
    }

    [Fact]
    public void CastSkill_OnCooldown_IsRejected_NoStateChange()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnDuel(config, new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)), new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2)));
        state = StartSkillCooldown(state, new EntityId(1), new SkillId(10), 5);

        ZoneState zoneBefore = Assert.Single(state.Zones);
        EntityState casterBefore = Assert.Single(zoneBefore.Entities.Where(e => e.Id.Value == 1));

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(cast)));

        Assert.Empty(state.SkillCastIntents);
        ZoneState zoneAfter = Assert.Single(state.Zones);
        EntityState casterAfter = Assert.Single(zoneAfter.Entities.Where(e => e.Id.Value == 1));
        Assert.Equal(casterBefore.SkillCooldowns, casterAfter.SkillCooldowns);
    }

    [Fact]
    public void CastSkill_OutOfRange_IsRejected()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnDuel(config, new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)), new Vec2Fix(Fix32.FromInt(12), Fix32.FromInt(2)));

        ZoneState zoneBefore = Assert.Single(state.Zones);
        EntityState casterBefore = Assert.Single(zoneBefore.Entities.Where(e => e.Id.Value == 1));

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(cast)));

        Assert.Empty(state.SkillCastIntents);
        ZoneState zoneAfter = Assert.Single(state.Zones);
        EntityState casterAfter = Assert.Single(zoneAfter.Entities.Where(e => e.Id.Value == 1));
        Assert.Equal(casterBefore.SkillCooldowns, casterAfter.SkillCooldowns);
    }

    [Fact]
    public void CastSkill_OrderIsDeterministic_SameTickMultipleCasters()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnDuel(config, new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)), new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2)));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(3), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(6), Fix32.FromInt(2))))));

        WorldCommand castFrom3 = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(3),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(11),
            TargetKind: CastTargetKind.Self);

        WorldCommand castFrom1 = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(castFrom3, castFrom1)));

        Assert.Equal(2, state.SkillCastIntents.Length);
        Assert.Equal(1, state.SkillCastIntents[0].CasterId.Value);
        Assert.Equal(3, state.SkillCastIntents[1].CasterId.Value);
    }

    private static WorldState StartSkillCooldown(WorldState state, EntityId casterId, SkillId skillId, int ticks)
    {
        ZoneState zone = Assert.Single(state.Zones);
        EntityState caster = Assert.Single(zone.Entities.Where(e => e.Id.Value == casterId.Value));
        EntityState updated = caster with { SkillCooldowns = caster.SkillCooldowns.StartCooldown(skillId, ticks) };
        ZoneState updatedZone = zone.WithEntities(zone.Entities.Select(e => e.Id.Value == casterId.Value ? updated : e).ToImmutableArray());
        return state.WithZoneUpdated(updatedZone);
    }

    private static WorldState SpawnDuel(SimulationConfig config, Vec2Fix p1, Vec2Fix p2)
    {
        TileMap map = BuildOpenMap(config.MapWidth, config.MapHeight);
        WorldState state = new(
            Tick: 0,
            Zones: ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            EntityLocations: ImmutableArray<EntityLocation>.Empty,
            LootEntities: ImmutableArray<LootEntityState>.Empty);

        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: p1),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: p2))));
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
                new SkillDefinition(new SkillId(10), Fix32.FromInt(6).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 6, ResourceCost: 0, TargetKind: CastTargetKind.Entity),
                new SkillDefinition(new SkillId(11), Fix32.FromInt(6).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 6, ResourceCost: 0, TargetKind: CastTargetKind.Self)),
            Invariants: InvariantOptions.Enabled);
    }
}
