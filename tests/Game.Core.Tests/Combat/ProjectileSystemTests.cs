using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class ProjectileSystemTests
{
    [Fact]
    public void Projectile_SpawnsDeterministically_AssignsIdsInOrder()
    {
        SimulationConfig config = Config() with { MaxProjectilesPerZone = 16 };
        WorldState state = Spawn(config);

        ImmutableArray<WorldCommand> commands = ImmutableArray.Create(
            Cast(2, 1),
            Cast(1, 2));

        state = Simulation.Step(config, state, new Inputs(commands));
        ZoneState zone = Assert.Single(state.Zones);

        Assert.Equal(new[] { 1, 2 }, zone.Projectiles.Select(p => p.ProjectileId).ToArray());
    }

    [Fact]
    public void Projectile_MovesDeterministically_ReachesTargetInExpectedTicks()
    {
        SimulationConfig config = Config();
        WorldState state = Spawn(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(Cast(1, 2))));
        Assert.Single(Assert.Single(state.Zones).Projectiles);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        Assert.Empty(Assert.Single(state.Zones).Projectiles);
    }

    [Fact]
    public void Projectile_OnHit_AppliesDamageAndDespawns()
    {
        SimulationConfig config = Config();
        WorldState state = Spawn(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(Cast(1, 2))));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        ZoneState zone = Assert.Single(state.Zones);
        EntityState target = zone.Entities.Single(e => e.Id.Value == 2);
        Assert.Equal(89, target.Hp);
        Assert.Empty(zone.Projectiles);
        Assert.Contains(state.ProjectileEvents, e => e.Kind == ProjectileEventKind.Hit);
        Assert.Contains(state.CombatLogEvents, e => e.SourceId.Value == 1 && e.TargetId.Value == 2 && e.Kind == CombatLogKind.Damage);
    }

    [Fact]
    public void Projectile_TargetDiesBeforeHit_HandlesGracefully()
    {
        SimulationConfig config = Config();
        WorldState state = Spawn(config);
        ZoneState zone = Assert.Single(state.Zones);
        state = state.WithZoneUpdated(zone.WithEntities(zone.Entities.Select(e => e.Id.Value == 2 ? e with { Hp = 1 } : e).ToImmutableArray()));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(Cast(1, 2))));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        Assert.Empty(Assert.Single(state.Zones).Projectiles);
    }

    [Fact]
    public void Projectile_WorldCollision_DespawnsIfBlocked()
    {
        SimulationConfig config = Config(collides: true);
        WorldState state = Spawn(config, blockedBetween: true);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(Cast(1, 2))));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        Assert.Empty(Assert.Single(state.Zones).Projectiles);
        Assert.DoesNotContain(state.CombatEvents, e => e.TargetId.Value == 2);
    }

    [Fact]
    public void ProjectileBudget_Exceed_DropsSpawnsDeterministically()
    {
        SimulationConfig config = Config() with { MaxProjectilesPerZone = 1 };
        WorldState state = Spawn(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(Cast(1, 2), Cast(2, 1))));

        Assert.Single(Assert.Single(state.Zones).Projectiles);
        Assert.Equal((uint)1, state.ProjectileSpawnsDropped_LastTick);
    }


    private static WorldCommand Cast(int caster, int target) => new(
        WorldCommandKind.CastSkill,
        new EntityId(caster),
        new ZoneId(1),
        SkillId: new SkillId(10),
        TargetKind: CastTargetKind.Entity,
        TargetEntityId: new EntityId(target));

    private static SimulationConfig Config(bool collides = false)
    {
        return SimulationConfig.Default(99) with
        {
            MapWidth = 16,
            MapHeight = 16,
            NpcCountPerZone = 0,
            SkillDefinitions = ImmutableArray.Create(new SkillDefinition(
                new SkillId(10),
                RangeRaw: Fix32.FromInt(8).Raw,
                HitRadiusRaw: Fix32.OneRaw,
                CooldownTicks: 1,
                ResourceCost: 0,
                TargetType: SkillTargetType.Entity,
                BaseDamage: 11,
                ProjectileSpeedRaw: Fix32.FromInt(1).Raw,
                UsesProjectile: true,
                CollidesWithWorld: collides))
        };
    }

    private static WorldState Spawn(SimulationConfig config, bool blockedBetween = false)
    {
        TileMap map = BuildMap(config.MapWidth, config.MapHeight, blockedBetween);
        WorldState state = new(
            0,
            ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            ImmutableArray<EntityLocation>.Empty,
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            CombatEvents: ImmutableArray<CombatEvent>.Empty);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2))))));

        return state;
    }

    private static TileMap BuildMap(int width, int height, bool blockedBetween)
    {
        ImmutableArray<TileKind>.Builder tiles = ImmutableArray.CreateBuilder<TileKind>(width * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool blocker = blockedBetween && x == 3 && y == 2;
                tiles.Add(border || blocker ? TileKind.Solid : TileKind.Empty);
            }
        }

        return new TileMap(width, height, tiles.MoveToImmutable());
    }
}
