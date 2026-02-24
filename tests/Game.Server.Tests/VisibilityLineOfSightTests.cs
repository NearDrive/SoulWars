using System.Linq;
using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Server.Tests;

public sealed class VisibilityLineOfSightTests
{
    [Fact]
    [Trait("Category", "PR77")]
    public void VisibilityBlockedByWallTests()
    {
        TileMap map = BuildMap(
            width: 7,
            height: 7,
            blockedTiles:
            [
                (3, 1),
                (3, 2),
                (3, 3),
                (3, 4),
                (3, 5)
            ]);

        EntityState viewer = new(
            Id: new EntityId(1),
            Pos: new Vec2Fix(Fix32.FromInt(1), Fix32.FromInt(3)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: 0,
            FactionId: new FactionId(1),
            VisionRadiusTiles: 5);

        WorldState world = BuildWorld(map, viewer);
        WorldState next = Simulation.Step(SimulationConfig.Default(seed: 77) with { NpcCountPerZone = 0 }, world, new Inputs(ImmutableArray<WorldCommand>.Empty));

        VisibilityGrid visibility = next.Zones.Single().Visibility;
        FactionId faction = new(1);

        Assert.True(visibility.IsVisible(faction, 2, 3));
        Assert.True(visibility.IsVisible(faction, 1, 5));
        Assert.False(visibility.IsVisible(faction, 5, 3));
        Assert.False(visibility.IsVisible(faction, 4, 3));
    }

    [Fact]
    [Trait("Category", "PR77")]
    public void DeterministicRaycastVisibilityTests()
    {
        TileMap map = BuildMap(
            width: 8,
            height: 8,
            blockedTiles:
            [
                (3, 2),
                (3, 3),
                (3, 4),
                (5, 4),
                (5, 5)
            ]);

        EntityState viewerA = new(
            Id: new EntityId(1),
            Pos: new Vec2Fix(Fix32.FromInt(1), Fix32.FromInt(1)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: 0,
            FactionId: new FactionId(1),
            VisionRadiusTiles: 6);

        EntityState viewerB = new(
            Id: new EntityId(2),
            Pos: new Vec2Fix(Fix32.FromInt(6), Fix32.FromInt(6)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: 0,
            FactionId: new FactionId(1),
            VisionRadiusTiles: 6);

        WorldState run1 = BuildWorld(map, viewerA, viewerB);
        WorldState run2 = BuildWorld(map, viewerA, viewerB);
        SimulationConfig config = SimulationConfig.Default(seed: 177) with { NpcCountPerZone = 0 };

        for (int i = 0; i < 6; i++)
        {
            run1 = Simulation.Step(config, run1, new Inputs(ImmutableArray<WorldCommand>.Empty));
            run2 = Simulation.Step(config, run2, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        ZoneState zone1 = run1.Zones.Single();
        ZoneState zone2 = run2.Zones.Single();

        byte[] faction1BytesRun1 = zone1.Visibility.GetPackedBytes(new FactionId(1));
        byte[] faction1BytesRun2 = zone2.Visibility.GetPackedBytes(new FactionId(1));

        Assert.Equal(faction1BytesRun1, faction1BytesRun2);
        Assert.Equal(StateChecksum.Compute(run1), StateChecksum.Compute(run2));
    }

    private static WorldState BuildWorld(TileMap map, params EntityState[] entities)
    {
        ImmutableArray<EntityState> allEntities = entities.ToImmutableArray();
        ZoneState zone = new(new ZoneId(1), map, allEntities);

        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: allEntities.Select(e => new EntityLocation(e.Id, new ZoneId(1))).ToImmutableArray(),
            LootEntities: ImmutableArray<LootEntityState>.Empty);
    }

    private static TileMap BuildMap(int width, int height, params (int X, int Y)[] blockedTiles)
    {
        TileKind[] tiles = Enumerable.Repeat(TileKind.Empty, width * height).ToArray();
        for (int i = 0; i < blockedTiles.Length; i++)
        {
            (int x, int y) = blockedTiles[i];
            tiles[(y * width) + x] = TileKind.Solid;
        }

        return new TileMap(width, height, tiles.ToImmutableArray());
    }
}
