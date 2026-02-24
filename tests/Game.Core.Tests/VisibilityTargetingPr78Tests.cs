using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Xunit;
using static Game.Core.Tests.VisibilityTargetingPr78TestHelpers;

namespace Game.Core.Tests;

[Trait("Category", "PR78")]
public sealed class TargetRestrictionTests
{
    [Fact]
    public void EntityTargetCast_Fails_WhenTargetNotVisibleToCasterFaction()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = BuildWorld(
            BuildMap(7, 7, (2, 3)),
            CreateEntity(new EntityId(1), 1, 3, new FactionId(1), visionRadius: 4),
            CreateEntity(new EntityId(2), 3, 3, new FactionId(2), visionRadius: 4));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        ZoneState zoneBefore = Assert.Single(state.Zones);
        EntityState targetBefore = Assert.Single(zoneBefore.Entities.Where(e => e.Id.Value == 2));
        EntityState casterBefore = Assert.Single(zoneBefore.Entities.Where(e => e.Id.Value == 1));
        Assert.False(zoneBefore.Visibility.IsVisible(new FactionId(1), 3, 3));

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(10),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(2));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(cast)));

        ZoneState zoneAfter = Assert.Single(state.Zones);
        EntityState targetAfter = Assert.Single(zoneAfter.Entities.Where(e => e.Id.Value == 2));
        EntityState casterAfter = Assert.Single(zoneAfter.Entities.Where(e => e.Id.Value == 1));

        Assert.Empty(state.SkillCastIntents);
        Assert.Equal(targetBefore.Hp, targetAfter.Hp);
        Assert.True(casterAfter.SkillCooldowns.IsReady(new SkillId(10)));
        Assert.Equal(casterBefore.LastAttackTick, casterAfter.LastAttackTick);
    }
}

[Trait("Category", "PR78")]
public sealed class AreaSkillBypassVisibilityTests
{
    [Fact]
    public void PointAoeCast_Accepts_AndCanHitInvisibleEnemy()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = BuildWorld(
            BuildMap(7, 7, (2, 3)),
            CreateEntity(new EntityId(1), 1, 3, new FactionId(1), visionRadius: 4),
            CreateEntity(new EntityId(2), 3, 3, new FactionId(2), visionRadius: 4));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        ZoneState zoneBefore = Assert.Single(state.Zones);
        EntityState targetBefore = Assert.Single(zoneBefore.Entities.Where(e => e.Id.Value == 2));
        Assert.False(zoneBefore.Visibility.IsVisible(new FactionId(1), 3, 3));

        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(1),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(11),
            TargetKind: CastTargetKind.Point,
            TargetPosXRaw: Fix32.FromInt(1).Raw,
            TargetPosYRaw: Fix32.FromInt(3).Raw);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(cast)));

        ZoneState zoneAfter = Assert.Single(state.Zones);
        EntityState targetAfter = Assert.Single(zoneAfter.Entities.Where(e => e.Id.Value == 2));

        Assert.Single(state.SkillCastIntents);
        Assert.Equal(targetBefore.Hp - 7, targetAfter.Hp);
    }
}

public sealed class ReplayVerify_VisibilityScenario
{
    [Fact]
    [Trait("Category", "PR78")]
    [Trait("Category", "ReplayVerify")]
    public void Replay_IsStable_WhenEntityTargetIsHidden_ButPointAoeWorks()
    {
        ScenarioRunResult baseline = Run(restartTick: null);
        ScenarioRunResult resumed = Run(restartTick: 4);

        Assert.Equal(baseline.FinalChecksum, resumed.FinalChecksum);
        Assert.Contains(0, baseline.IntentCountByTick);
        Assert.Contains(1, baseline.IntentCountByTick);
    }

    private static ScenarioRunResult Run(int? restartTick)
    {
        SimulationConfig config = CreateConfig();
        WorldState state = BuildWorld(
            BuildMap(7, 7, (2, 3)),
            CreateEntity(new EntityId(1), 1, 3, new FactionId(1), visionRadius: 4),
            CreateEntity(new EntityId(2), 3, 3, new FactionId(2), visionRadius: 4));

        ImmutableArray<int>.Builder intentCountByTick = ImmutableArray.CreateBuilder<int>();

        for (int tick = 0; tick < 8; tick++)
        {
            ImmutableArray<WorldCommand> commands = tick switch
            {
                1 => ImmutableArray.Create(new WorldCommand(
                    WorldCommandKind.CastSkill,
                    new EntityId(1),
                    new ZoneId(1),
                    SkillId: new SkillId(10),
                    TargetKind: CastTargetKind.Entity,
                    TargetEntityId: new EntityId(2))),
                3 => ImmutableArray.Create(new WorldCommand(
                    WorldCommandKind.CastSkill,
                    new EntityId(1),
                    new ZoneId(1),
                    SkillId: new SkillId(11),
                    TargetKind: CastTargetKind.Point,
                    TargetPosXRaw: Fix32.FromInt(1).Raw,
                    TargetPosYRaw: Fix32.FromInt(3).Raw)),
                _ => ImmutableArray<WorldCommand>.Empty
            };

            state = Simulation.Step(config, state, new Inputs(commands));
            intentCountByTick.Add(state.SkillCastIntents.Length);

            if (restartTick.HasValue && tick + 1 == restartTick.Value)
            {
                byte[] snapshot = Game.Persistence.WorldStateSerializer.SaveToBytes(state);
                state = Game.Persistence.WorldStateSerializer.LoadFromBytes(snapshot);
            }
        }

        return new ScenarioRunResult(StateChecksum.Compute(state), intentCountByTick.ToImmutable());
    }

    private sealed record ScenarioRunResult(string FinalChecksum, ImmutableArray<int> IntentCountByTick);
}

internal static class VisibilityTargetingPr78TestHelpers
{
    public static TileMap BuildMap(int width, int height, params (int X, int Y)[] blockedTiles)
    {
        TileKind[] tiles = Enumerable.Repeat(TileKind.Empty, width * height).ToArray();
        for (int i = 0; i < blockedTiles.Length; i++)
        {
            (int x, int y) = blockedTiles[i];
            tiles[(y * width) + x] = TileKind.Solid;
        }

        return new TileMap(width, height, tiles.ToImmutableArray());
    }

    public static EntityState CreateEntity(EntityId id, int x, int y, FactionId factionId, int visionRadius)
        => new(
            Id: id,
            Pos: new Vec2Fix(Fix32.FromInt(x), Fix32.FromInt(y)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 1,
            LastAttackTick: -1,
            FactionId: factionId,
            VisionRadiusTiles: visionRadius);

    public static WorldState BuildWorld(TileMap map, params EntityState[] entities)
    {
        ImmutableArray<EntityState> allEntities = entities.ToImmutableArray();
        ZoneState zone = new(new ZoneId(1), map, allEntities);

        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: allEntities.Select(e => new EntityLocation(e.Id, new ZoneId(1))).ToImmutableArray(),
            LootEntities: ImmutableArray<LootEntityState>.Empty);
    }

    public static SimulationConfig CreateConfig()
        => SimulationConfig.Default(seed: 7801) with
        {
            NpcCountPerZone = 0,
            MapWidth = 7,
            MapHeight = 7,
            SkillDefinitions = ImmutableArray.Create(
                new SkillDefinition(new SkillId(10), Fix32.FromInt(6).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 6, ResourceCost: 0, TargetKind: CastTargetKind.Entity, BaseAmount: 5),
                new SkillDefinition(new SkillId(11), Fix32.FromInt(6).Raw, HitRadiusRaw: Fix32.FromInt(3).Raw, CooldownTicks: 6, ResourceCost: 0, TargetKind: CastTargetKind.Point, BaseAmount: 7))
        };
}
