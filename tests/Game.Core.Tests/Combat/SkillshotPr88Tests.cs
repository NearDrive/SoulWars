using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

[Trait("Category", "PR88")]
public sealed class SkillshotPr88Tests
{
    [Fact]
    [Trait("Category", "PR88")]
    [Trait("Category", "Canary")]
    public void Skillshot_SpawnAndDespawn_Stable()
    {
        SimulationConfig config = Config();
        WorldState state = Spawn(config, new[] { (1, 2, 2), (2, 6, 2) });

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(CastPoint(1, 8, 2))));
        ZoneState zone = Assert.Single(state.Zones);
        ProjectileComponent projectile = Assert.Single(zone.Projectiles);
        Assert.Equal(1, projectile.ProjectileId);

        for (int i = 0; i < config.MaxProjectileLifetimeTicks + 2; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        zone = Assert.Single(state.Zones);
        Assert.Empty(zone.Projectiles);
        Assert.Equal(zone.Projectiles.OrderBy(p => p.ProjectileId).Select(p => p.ProjectileId), zone.Projectiles.Select(p => p.ProjectileId));
    }

    [Fact]
    [Trait("Category", "PR88")]
    [Trait("Category", "Canary")]
    public void Skillshot_HitEvent_EmittedAndCanonical()
    {
        SimulationConfig config = Config();
        WorldState state = Spawn(config, new[] { (1, 2, 2), (2, 4, 2) });

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(CastPoint(1, 6, 2))));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        ProjectileEvent[] hitEvents = state.ProjectileEvents.Where(e => e.Kind == ProjectileEventKind.Hit).ToArray();
        ProjectileEvent hit = Assert.Single(hitEvents);
        Assert.Equal(state.Tick, hit.Tick);
        Assert.Equal(1, hit.OwnerId.Value);
        Assert.Equal(2, hit.TargetId.Value);
        Assert.Equal(1, hit.AbilityId.Value);

        ProjectileEvent[] canonical = state.ProjectileEvents
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.ProjectileId)
            .ThenBy(e => (int)e.Kind)
            .ThenBy(e => e.OwnerId.Value)
            .ThenBy(e => e.TargetId.Value)
            .ThenBy(e => e.AbilityId.Value)
            .ToArray();
        Assert.Equal(canonical, state.ProjectileEvents.ToArray());
    }

    [Fact]
    [Trait("Category", "PR88")]
    [Trait("Category", "Canary")]
    public void Skillshot_MultipleTargets_FirstHitDeterministic()
    {
        SimulationConfig config = Config();
        WorldState state = Spawn(config, new[] { (1, 2, 2), (3, 4, 2), (2, 4, 2) });

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(CastPoint(1, 6, 2))));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        ProjectileEvent hit = Assert.Single(state.ProjectileEvents.Where(e => e.Kind == ProjectileEventKind.Hit));
        Assert.Equal(2, hit.TargetId.Value);
    }

    private static WorldCommand CastPoint(int casterId, int x, int y) => new(
        WorldCommandKind.CastSkill,
        new EntityId(casterId),
        new ZoneId(1),
        SkillId: new SkillId(1),
        TargetKind: CastTargetKind.Point,
        TargetPosXRaw: Fix32.FromInt(x).Raw,
        TargetPosYRaw: Fix32.FromInt(y).Raw);

    private static SimulationConfig Config() => SimulationConfig.Default(8801) with
    {
        MapWidth = 16,
        MapHeight = 16,
        NpcCountPerZone = 0,
        MaxProjectileLifetimeTicks = 4,
        SkillDefinitions = ImmutableArray.Create(new SkillDefinition(
            Id: new SkillId(1),
            RangeQRaw: Fix32.FromInt(64).Raw,
            HitRadiusRaw: Fix32.OneRaw,
            MaxTargets: 1,
            CooldownTicks: 1,
            CastTimeTicks: 0,
            GlobalCooldownTicks: 0,
            ResourceCost: 0,
            TargetKind: CastTargetKind.Point,
            BaseAmount: 0,
            ProjectileSpeedRaw: Fix32.FromInt(1).Raw,
            UsesProjectile: true,
            CollidesWithWorld: false))
    };

    private static WorldState Spawn(SimulationConfig config, IEnumerable<(int id, int x, int y)> spawns)
    {
        TileMap map = BuildMap(config.MapWidth, config.MapHeight);
        WorldState state = new(
            0,
            ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            ImmutableArray<EntityLocation>.Empty,
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            CombatEvents: ImmutableArray<CombatEvent>.Empty);

        ImmutableArray<WorldCommand> enters = spawns
            .Select(spawn => new WorldCommand(WorldCommandKind.EnterZone, new EntityId(spawn.id), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(spawn.x), Fix32.FromInt(spawn.y))))
            .OrderBy(command => command.EntityId.Value)
            .ToImmutableArray();

        return Simulation.Step(config, state, new Inputs(enters));
    }

    private static TileMap BuildMap(int width, int height)
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
}
