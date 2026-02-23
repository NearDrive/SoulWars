using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

public sealed class VisibilityGridDeterminismTests
{
    [Fact]
    [Trait("Category", "PR75")]
    public void TwoRuns_SameSetup_ProduceSameVisibilityAndChecksum()
    {
        WorldState runA = RunDeterministicSequence();
        WorldState runB = RunDeterministicSequence();

        Assert.Equal(StateChecksum.Compute(runA), StateChecksum.Compute(runB));

        ZoneState zoneA = Assert.Single(runA.Zones);
        ZoneState zoneB = Assert.Single(runB.Zones);

        ImmutableArray<FactionId> factionsA = zoneA.Visibility.GetFactionIdsOrdered();
        ImmutableArray<FactionId> factionsB = zoneB.Visibility.GetFactionIdsOrdered();
        Assert.Equal(factionsA, factionsB);

        for (int i = 0; i < factionsA.Length; i++)
        {
            byte[] bytesA = zoneA.Visibility.GetPackedBytes(factionsA[i]);
            byte[] bytesB = zoneB.Visibility.GetPackedBytes(factionsB[i]);
            Assert.Equal(bytesA, bytesB);
        }

        byte[] snapshot = WorldStateSerializer.SaveToBytes(runA);
        WorldState restored = WorldStateSerializer.LoadFromBytes(snapshot);

        Assert.Equal(StateChecksum.Compute(runA), StateChecksum.Compute(restored));
    }

    private static WorldState RunDeterministicSequence()
    {
        SimulationConfig config = CreateConfig(5757);
        WorldState state = BuildWorld();

        for (int i = 0; i < 4; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        return state;
    }

    private static WorldState BuildWorld()
    {
        TileMap map = BuildOpenMap(8, 8);
        EntityState e1 = new(
            Id: new EntityId(1),
            Pos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: -1,
            Kind: EntityKind.Player,
            FactionId: new FactionId(1),
            VisionRadiusTiles: 1);

        EntityState e2 = new(
            Id: new EntityId(2),
            Pos: new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(5)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: -1,
            Kind: EntityKind.Player,
            FactionId: new FactionId(2),
            VisionRadiusTiles: 2);

        ZoneState zone = new(new ZoneId(1), map, ImmutableArray.Create(e1, e2));
        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: ImmutableArray.Create(
                new EntityLocation(e1.Id, new ZoneId(1)),
                new EntityLocation(e2.Id, new ZoneId(1))));
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
        MapWidth: 8,
        MapHeight: 8,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}

public sealed class VisibilityTileSetConsistencyTests
{
    [Fact]
    [Trait("Category", "PR75")]
    public void ChebyshevRadius_MarksExpectedTiles()
    {
        TileMap map = BuildOpenMap(8, 8);
        FactionId faction = new(7);
        EntityState watcher = new(
            Id: new EntityId(10),
            Pos: new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(3)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: 0,
            Kind: EntityKind.Player,
            FactionId: faction,
            VisionRadiusTiles: 1);

        WorldState state = new(
            Tick: 0,
            Zones: ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray.Create(watcher))),
            EntityLocations: ImmutableArray.Create(new EntityLocation(watcher.Id, new ZoneId(1))));

        state = Simulation.Step(CreateConfig(42), state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        ZoneState zone = Assert.Single(state.Zones);
        int visibleCount = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (zone.Visibility.IsVisible(faction, x, y))
                {
                    visibleCount++;
                }
            }
        }

        Assert.Equal(9, visibleCount);
        Assert.True(zone.Visibility.IsVisible(faction, 3, 3));
        Assert.True(zone.Visibility.IsVisible(faction, 2, 2));
        Assert.True(zone.Visibility.IsVisible(faction, 4, 4));
        Assert.False(zone.Visibility.IsVisible(faction, 1, 3));
        Assert.False(zone.Visibility.IsVisible(faction, 5, 3));
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
        MapWidth: 8,
        MapHeight: 8,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
