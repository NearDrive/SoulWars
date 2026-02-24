using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class SharedVisionMergeTests
{
    [Fact]
    [Trait("Category", "PR76")]
    public void SameFactionEntities_MergeVisibilityAsUnion()
    {
        FactionId faction = new(11);
        EntityState leftWatcher = SharedFactionVisionPr76TestHelpers.CreateWatcher(new EntityId(1), 1, 1, faction, visionRadiusTiles: 1);
        EntityState rightWatcher = SharedFactionVisionPr76TestHelpers.CreateWatcher(new EntityId(2), 5, 1, faction, visionRadiusTiles: 1);

        WorldState state = SharedFactionVisionPr76TestHelpers.CreateWorld(
            zoneOneEntities: ImmutableArray.Create(leftWatcher, rightWatcher));

        WorldState next = Simulation.Step(SharedFactionVisionPr76TestHelpers.CreateConfig(seed: 7601, zoneCount: 1), state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        ZoneState zone = Assert.Single(next.Zones);

        Assert.True(zone.Visibility.IsVisible(faction, 1, 1));
        Assert.True(zone.Visibility.IsVisible(faction, 0, 0));
        Assert.True(zone.Visibility.IsVisible(faction, 2, 2));

        Assert.True(zone.Visibility.IsVisible(faction, 5, 1));
        Assert.True(zone.Visibility.IsVisible(faction, 4, 0));
        Assert.True(zone.Visibility.IsVisible(faction, 6, 2));

        Assert.False(zone.Visibility.IsVisible(faction, 3, 1));
    }
}

public sealed class CrossFactionIsolationTests
{
    [Fact]
    [Trait("Category", "PR76")]
    public void DifferentFactions_DoNotBleedVisibility()
    {
        FactionId factionA = new(1);
        FactionId factionB = new(2);

        EntityState watcherA = SharedFactionVisionPr76TestHelpers.CreateWatcher(new EntityId(10), 1, 1, factionA, visionRadiusTiles: 1);
        EntityState watcherB = SharedFactionVisionPr76TestHelpers.CreateWatcher(new EntityId(20), 6, 6, factionB, visionRadiusTiles: 1);

        WorldState state = SharedFactionVisionPr76TestHelpers.CreateWorld(
            zoneOneEntities: ImmutableArray.Create(watcherA, watcherB));

        WorldState next = Simulation.Step(SharedFactionVisionPr76TestHelpers.CreateConfig(seed: 7602, zoneCount: 1), state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        ZoneState zone = Assert.Single(next.Zones);

        Assert.True(zone.Visibility.IsVisible(factionA, 1, 1));
        Assert.False(zone.Visibility.IsVisible(factionA, 6, 6));

        Assert.True(zone.Visibility.IsVisible(factionB, 6, 6));
        Assert.False(zone.Visibility.IsVisible(factionB, 1, 1));
    }
}

public sealed class ZoneLocalIsolationTests
{
    [Fact]
    [Trait("Category", "PR76")]
    public void SameFactionAcrossZones_IsIsolatedPerZone()
    {
        FactionId sharedFaction = new(42);

        EntityState zoneAWatcher = SharedFactionVisionPr76TestHelpers.CreateWatcher(new EntityId(100), 1, 1, sharedFaction, visionRadiusTiles: 1);
        EntityState zoneBWatcher = SharedFactionVisionPr76TestHelpers.CreateWatcher(new EntityId(200), 6, 6, sharedFaction, visionRadiusTiles: 1);

        WorldState state = SharedFactionVisionPr76TestHelpers.CreateWorld(
            zoneOneEntities: ImmutableArray.Create(zoneAWatcher),
            zoneTwoEntities: ImmutableArray.Create(zoneBWatcher));

        WorldState next = Simulation.Step(SharedFactionVisionPr76TestHelpers.CreateConfig(seed: 7603, zoneCount: 2), state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        ZoneState zoneA = Assert.Single(next.Zones.Where(z => z.Id.Value == 1));
        ZoneState zoneB = Assert.Single(next.Zones.Where(z => z.Id.Value == 2));

        Assert.True(zoneA.Visibility.IsVisible(sharedFaction, 1, 1));
        Assert.False(zoneA.Visibility.IsVisible(sharedFaction, 6, 6));

        Assert.True(zoneB.Visibility.IsVisible(sharedFaction, 6, 6));
        Assert.False(zoneB.Visibility.IsVisible(sharedFaction, 1, 1));
    }
}

file static class SharedFactionVisionPr76TestHelpers
{
    public static EntityState CreateWatcher(EntityId id, int x, int y, FactionId factionId, int visionRadiusTiles)
        => new(
            Id: id,
            Pos: new Vec2Fix(Fix32.FromInt(x), Fix32.FromInt(y)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: -1,
            Kind: EntityKind.Player,
            FactionId: factionId,
            VisionRadiusTiles: visionRadiusTiles);

    public static WorldState CreateWorld(ImmutableArray<EntityState> zoneOneEntities, ImmutableArray<EntityState>? zoneTwoEntities = null)
    {
        TileMap map = BuildOpenMap(8, 8);
        ZoneState zoneOne = new(new ZoneId(1), map, zoneOneEntities);

        if (zoneTwoEntities is null)
        {
            return new WorldState(
                Tick: 0,
                Zones: ImmutableArray.Create(zoneOne),
                EntityLocations: zoneOneEntities.Select(entity => new EntityLocation(entity.Id, zoneOne.Id)).ToImmutableArray());
        }

        ZoneState zoneTwo = new(new ZoneId(2), map, zoneTwoEntities.Value);
        ImmutableArray<EntityLocation> locations = zoneOneEntities
            .Select(entity => new EntityLocation(entity.Id, zoneOne.Id))
            .Concat(zoneTwoEntities.Value.Select(entity => new EntityLocation(entity.Id, zoneTwo.Id)))
            .ToImmutableArray();

        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zoneOne, zoneTwo),
            EntityLocations: locations);
    }

    public static SimulationConfig CreateConfig(int seed, int zoneCount) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: zoneCount,
        MapWidth: 8,
        MapHeight: 8,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);

    private static TileMap BuildOpenMap(int width, int height)
    {
        ImmutableArray<TileKind>.Builder tiles = ImmutableArray.CreateBuilder<TileKind>(width * height);
        for (int i = 0; i < width * height; i++)
        {
            tiles.Add(TileKind.Empty);
        }

        return new TileMap(width, height, tiles.MoveToImmutable());
    }
}
