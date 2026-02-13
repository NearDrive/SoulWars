using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class CoreInvariantsTests
{
    [Fact]
    public void Validate_Throws_WhenZonesUnsorted()
    {
        TileMap map = new(2, 2, ImmutableArray.Create(TileKind.Empty, TileKind.Empty, TileKind.Empty, TileKind.Empty));
        WorldState world = new(0, ImmutableArray.Create(
            new ZoneState(new ZoneId(2), map, ImmutableArray<EntityState>.Empty),
            new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)));

        Assert.Throws<InvariantViolationException>(() => CoreInvariants.Validate(world));
    }
}
