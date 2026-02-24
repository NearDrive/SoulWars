using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR79")]
public sealed class VisibilitySnapshotRoundtripTests
{
    [Fact]
    public void SnapshotReload_PreservesVisibilityBitsets_AndChecksum()
    {
        SimulationConfig config = CreateConfig(seed: 7901);
        WorldState state = BuildWorld();

        for (int i = 0; i < 5; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        ZoneState beforeZone = Assert.Single(state.Zones);
        byte[] snapshot = WorldStateSerializer.SaveToBytes(state);
        WorldState restored = WorldStateSerializer.LoadFromBytes(snapshot);
        ZoneState afterZone = Assert.Single(restored.Zones);

        Assert.Equal(StateChecksum.Compute(state), StateChecksum.Compute(restored));
        Assert.Equal(beforeZone.Visibility.Width, afterZone.Visibility.Width);
        Assert.Equal(beforeZone.Visibility.Height, afterZone.Visibility.Height);

        ImmutableArray<FactionId> expectedFactions = beforeZone.Visibility.GetFactionIdsOrdered();
        ImmutableArray<FactionId> actualFactions = afterZone.Visibility.GetFactionIdsOrdered();
        Assert.Equal(expectedFactions.Select(f => f.Value), actualFactions.Select(f => f.Value));

        foreach (FactionId faction in expectedFactions)
        {
            Assert.Equal(beforeZone.Visibility.GetPackedBytes(faction), afterZone.Visibility.GetPackedBytes(faction));
        }
    }

    private static WorldState BuildWorld()
    {
        TileMap map = BuildOpenMap(10, 10);

        EntityState scoutA = new(
            Id: new EntityId(101),
            Pos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 1,
            AttackCooldownTicks: 10,
            LastAttackTick: -1,
            Kind: EntityKind.Player,
            FactionId: new FactionId(2),
            VisionRadiusTiles: 2);

        EntityState scoutB = new(
            Id: new EntityId(102),
            Pos: new Vec2Fix(Fix32.FromInt(7), Fix32.FromInt(7)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 1,
            AttackCooldownTicks: 10,
            LastAttackTick: -1,
            Kind: EntityKind.Player,
            FactionId: new FactionId(1),
            VisionRadiusTiles: 3);

        ZoneState zone = new(new ZoneId(1), map, ImmutableArray.Create(scoutA, scoutB));
        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: ImmutableArray.Create(
                new EntityLocation(scoutA.Id, new ZoneId(1)),
                new EntityLocation(scoutB.Id, new ZoneId(1))));
    }

    private static TileMap BuildOpenMap(int width, int height)
    {
        ImmutableArray<TileKind>.Builder tiles = ImmutableArray.CreateBuilder<TileKind>(width * height);
        for (int i = 0; i < width * height; i++)
        {
            tiles.Add(TileKind.Empty);
        }

        return new TileMap(width, height, tiles.MoveToImmutable());
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: 1,
        MapWidth: 10,
        MapHeight: 10,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
