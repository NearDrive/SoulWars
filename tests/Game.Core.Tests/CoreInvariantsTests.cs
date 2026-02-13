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

    [Fact]
    public void Validate_Throws_WhenDeadEntityPresent()
    {
        TileMap map = new(2, 2, ImmutableArray.Create(TileKind.Empty, TileKind.Empty, TileKind.Empty, TileKind.Empty));
        EntityState dead = new(
            Id: new EntityId(1),
            Pos: new Vec2Fix(Fix32.FromInt(0), Fix32.FromInt(0)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 0,
            IsAlive: false,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: -10);

        WorldState world = new(0, ImmutableArray.Create(
            new ZoneState(new ZoneId(1), map, ImmutableArray.Create(dead))));

        Assert.Throws<InvariantViolationException>(() => CoreInvariants.Validate(world));
    }
}
