using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR79")]
public sealed class VisibilityRestartDeterminismTests
{
    [Fact]
    public void SnapshotRestart_RunToTickN_MatchesNoRestartChecksum()
    {
        const int totalTicks = 120;
        const int splitTick = 55;

        SimulationConfig config = CreateConfig(seed: 7902);
        WorldState baseline = RunUntil(config, totalTicks, restartTick: null);
        WorldState resumed = RunUntil(config, totalTicks, restartTick: splitTick);

        Assert.Equal(StateChecksum.Compute(baseline), StateChecksum.Compute(resumed));
        Assert.Equal(StateChecksum.ComputeGlobalChecksum(baseline), StateChecksum.ComputeGlobalChecksum(resumed));
    }

    private static WorldState RunUntil(SimulationConfig config, int totalTicks, int? restartTick)
    {
        WorldState state = BuildWorld();

        for (int tick = 0; tick < totalTicks; tick++)
        {
            ImmutableArray<WorldCommand> commands = BuildCommandsForTick(tick);
            state = Simulation.Step(config, state, new Inputs(commands));

            if (restartTick.HasValue && tick + 1 == restartTick.Value)
            {
                byte[] snapshot = WorldStateSerializer.SaveToBytes(state);
                state = WorldStateSerializer.LoadFromBytes(snapshot);
            }
        }

        return state;
    }

    private static ImmutableArray<WorldCommand> BuildCommandsForTick(int tick)
    {
        if (tick % 20 == 0)
        {
            return ImmutableArray.Create(
                Move(101, x: 8, y: 1),
                Move(102, x: 1, y: 8));
        }

        if (tick % 20 == 10)
        {
            return ImmutableArray.Create(
                Move(101, x: 2, y: 2),
                Move(102, x: 7, y: 7));
        }

        return ImmutableArray<WorldCommand>.Empty;
    }

    private static WorldCommand Move(int entityId, int x, int y)
        => new(
            Kind: WorldCommandKind.Move,
            EntityId: new EntityId(entityId),
            ZoneId: new ZoneId(1),
            TargetPos: new Vec2Fix(Fix32.FromInt(x), Fix32.FromInt(y)));

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
