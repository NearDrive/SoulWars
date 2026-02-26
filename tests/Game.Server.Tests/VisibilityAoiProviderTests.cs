using System.Collections.Immutable;
using Game.Core;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "PR81")]
public sealed class VisibilityAoiProviderTests
{
    [Fact]
    [Trait("Category", "PR81")]
    public void VisibilityAoiProvider_ReturnsOnlyVisibleEntities_SortedByEntityId()
    {
        VisibilityAoiProvider provider = new();
        WorldState world = BuildWorld(
            new EntityState(new EntityId(10), At(1, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1)),
            new EntityState(new EntityId(30), At(3, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2)),
            new EntityState(new EntityId(20), At(2, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1)),
            new EntityState(new EntityId(40), At(5, 5), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2)),
            visibilitySetup: visibility =>
            {
                visibility.SetVisible(new FactionId(1), 1, 1);
                visibility.SetVisible(new FactionId(1), 2, 1);
            });

        VisibleSet result = provider.ComputeVisible(world, new ZoneId(1), new EntityId(10));

        Assert.Equal(new[] { 10, 20 }, result.EntityIds.Select(id => id.Value).ToArray());
    }

    [Fact]
    [Trait("Category", "PR81")]
    public void VisibilityAoiProvider_IsDeterministic_ForSameWorldState()
    {
        VisibilityAoiProvider provider = new();
        WorldState world = BuildWorld(
            new EntityState(new EntityId(7), At(1, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1)),
            new EntityState(new EntityId(8), At(2, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1)),
            new EntityState(new EntityId(9), At(3, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2)),
            visibilitySetup: visibility =>
            {
                visibility.SetVisible(new FactionId(1), 1, 1);
                visibility.SetVisible(new FactionId(1), 2, 1);
            });

        int[] run1 = provider.ComputeVisible(world, new ZoneId(1), new EntityId(7)).EntityIds.Select(id => id.Value).ToArray();
        int[] run2 = provider.ComputeVisible(world, new ZoneId(1), new EntityId(7)).EntityIds.Select(id => id.Value).ToArray();

        Assert.Equal(run1, run2);
    }

    private static WorldState BuildWorld(params EntityState[] entities)
        => BuildWorld(entities, _ => { });

    private static WorldState BuildWorld(EntityState[] entities, Action<VisibilityGrid> visibilitySetup)
    {
        TileMap map = new(8, 8, Enumerable.Repeat(TileKind.Empty, 64).ToImmutableArray());
        ZoneState zone = new(new ZoneId(1), map, entities.ToImmutableArray());
        visibilitySetup(zone.Visibility);

        ImmutableArray<EntityLocation> locations = entities
            .OrderBy(entity => entity.Id.Value)
            .Select(entity => new EntityLocation(entity.Id, zone.Id))
            .ToImmutableArray();

        return new WorldState(0, ImmutableArray.Create(zone), locations);
    }

    private static Vec2Fix At(int x, int y) => new(Fix32.FromInt(x), Fix32.FromInt(y));
}

[Trait("Category", "PR81")]
public sealed class CrossFactionIsolationAoiTests
{
    [Fact]
    [Trait("Category", "PR81")]
    public void VisibilityAoiProvider_ProducesFactionSpecificAoi_AndExcludesInvisibleEntities()
    {
        VisibilityAoiProvider provider = new();
        WorldState world = BuildWorld(
            new EntityState(new EntityId(11), At(1, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1)),
            new EntityState(new EntityId(12), At(2, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1)),
            new EntityState(new EntityId(21), At(5, 5), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2)),
            new EntityState(new EntityId(22), At(6, 5), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2)),
            visibilitySetup: visibility =>
            {
                visibility.SetVisible(new FactionId(1), 1, 1);
                visibility.SetVisible(new FactionId(1), 2, 1);
                visibility.SetVisible(new FactionId(2), 5, 5);
                visibility.SetVisible(new FactionId(2), 6, 5);
            });

        int[] faction1Aoi = provider.ComputeVisible(world, new ZoneId(1), new EntityId(11)).EntityIds.Select(id => id.Value).ToArray();
        int[] faction2Aoi = provider.ComputeVisible(world, new ZoneId(1), new EntityId(21)).EntityIds.Select(id => id.Value).ToArray();

        Assert.Equal(new[] { 11, 12 }, faction1Aoi);
        Assert.Equal(new[] { 21, 22 }, faction2Aoi);
        Assert.NotEqual(faction1Aoi, faction2Aoi);
        Assert.DoesNotContain(21, faction1Aoi);
        Assert.DoesNotContain(11, faction2Aoi);
    }

    private static WorldState BuildWorld(EntityState[] entities, Action<VisibilityGrid> visibilitySetup)
    {
        TileMap map = new(8, 8, Enumerable.Repeat(TileKind.Empty, 64).ToImmutableArray());
        ZoneState zone = new(new ZoneId(1), map, entities.ToImmutableArray());
        visibilitySetup(zone.Visibility);

        ImmutableArray<EntityLocation> locations = entities
            .OrderBy(entity => entity.Id.Value)
            .Select(entity => new EntityLocation(entity.Id, zone.Id))
            .ToImmutableArray();

        return new WorldState(0, ImmutableArray.Create(zone), locations);
    }

    private static Vec2Fix At(int x, int y) => new(Fix32.FromInt(x), Fix32.FromInt(y));
}
