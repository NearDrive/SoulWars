using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR70")]
public sealed class ChaseTargetMovementTests
{
    private static readonly Fix32 Half = new(Fix32.OneRaw / 2);

    [Fact]
    public void NpcChaseEntity_ProgressesWithoutCrossingBlockedTiles()
    {
        SimulationConfig config = CreateConfig(7001);
        WorldState state = Simulation.CreateInitialState(config, BuildChaseZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);
        EntityId targetId = new(1);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, targetId, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(8) + Half, Fix32.FromInt(8) + Half)))));

        Fix32 initialDistSq = DistanceSq(state, zoneId, npcId, targetId);
        Fix32 previousSample = initialDistSq;

        for (int tick = 0; tick < 80; tick++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
            ZoneState zone = Assert.Single(state.Zones);
            EntityState npc = zone.Entities.Single(e => e.Id == npcId);

            int npcTileX = Fix32.FloorToInt(npc.Pos.X);
            int npcTileY = Fix32.FloorToInt(npc.Pos.Y);
            Assert.NotEqual(TileKind.Solid, zone.Map.Get(npcTileX, npcTileY));

            if ((tick + 1) % 10 == 0)
            {
                Fix32 sampledDist = DistanceSq(state, zoneId, npcId, targetId);
                Assert.True(sampledDist <= previousSample);
                previousSample = sampledDist;
            }
        }

        Fix32 finalDistSq = DistanceSq(state, zoneId, npcId, targetId);
        Assert.True(finalDistSq < initialDistSq);
    }

    private static Fix32 DistanceSq(WorldState state, ZoneId zoneId, EntityId a, EntityId b)
    {
        Assert.True(state.TryGetZone(zoneId, out ZoneState zone));
        EntityState first = zone.Entities.Single(e => e.Id == a);
        EntityState second = zone.Entities.Single(e => e.Id == b);
        Vec2Fix diff = first.Pos - second.Pos;
        return diff.LengthSq();
    }

    private static ZoneDefinitions BuildChaseZone()
    {
        ImmutableArray<ZoneAabb>.Builder obstacles = ImmutableArray.CreateBuilder<ZoneAabb>();
        for (int y = 1; y <= 9; y++)
        {
            if (y != 5)
            {
                obstacles.Add(TileObstacle(5, y));
            }
        }

        ZoneDefinition zone = new(
            ZoneId: new ZoneId(1),
            Bounds: new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(11), Fix32.FromInt(11)),
            StaticObstacles: obstacles.MoveToImmutable(),
            NpcSpawns: ImmutableArray.Create(new NpcSpawnDefinition("npc.default", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(2) + Half, Fix32.FromInt(2) + Half)))),
            LootRules: null,
            RespawnPoint: new Vec2Fix(Fix32.FromInt(2) + Half, Fix32.FromInt(2) + Half));

        return new ZoneDefinitions(ImmutableArray.Create(zone));
    }

    private static ZoneAabb TileObstacle(int x, int y)
        => new(new Vec2Fix(Fix32.FromInt(x) + Half, Fix32.FromInt(y) + Half), new Vec2Fix(Half, Half));

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 12,
        MapHeight: 12,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(32),
        Invariants: InvariantOptions.Enabled);
}

[Trait("Category", "PR70")]
public sealed class CollisionRespectTests
{
    private static readonly Fix32 Half = new(Fix32.OneRaw / 2);

    [Fact]
    public void UnreachableTarget_NpcKeepsHoldAndRespectsWalls()
    {
        SimulationConfig config = CreateConfig(7002);
        WorldState state = Simulation.CreateInitialState(config, BuildSealedTargetZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);
        EntityId targetId = new(2);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, targetId, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(8) + Half, Fix32.FromInt(8) + Half)))));

        for (int tick = 0; tick < 40; tick++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
            ZoneState zone = Assert.Single(state.Zones);
            EntityState npc = zone.Entities.Single(e => e.Id == npcId);
            int npcTileX = Fix32.FloorToInt(npc.Pos.X);
            int npcTileY = Fix32.FloorToInt(npc.Pos.Y);
            Assert.NotEqual(TileKind.Solid, zone.Map.Get(npcTileX, npcTileY));
        }

        ZoneState finalZone = Assert.Single(state.Zones);
        EntityState finalNpc = finalZone.Entities.Single(e => e.Id == npcId);
        Assert.Equal(MoveIntentType.Hold, finalNpc.MoveIntent.Type);
        Assert.Equal(0, finalNpc.MoveIntent.PathLen);
    }

    private static ZoneDefinitions BuildSealedTargetZone()
    {
        ImmutableArray<ZoneAabb>.Builder obstacles = ImmutableArray.CreateBuilder<ZoneAabb>();
        for (int y = 6; y <= 10; y++)
        {
            for (int x = 6; x <= 10; x++)
            {
                if (x == 6 || x == 10 || y == 6 || y == 10)
                {
                    obstacles.Add(TileObstacle(x, y));
                }
            }
        }

        ZoneDefinition zone = new(
            ZoneId: new ZoneId(1),
            Bounds: new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(11), Fix32.FromInt(11)),
            StaticObstacles: obstacles.MoveToImmutable(),
            NpcSpawns: ImmutableArray.Create(new NpcSpawnDefinition("npc.default", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(2) + Half, Fix32.FromInt(2) + Half)))),
            LootRules: null,
            RespawnPoint: new Vec2Fix(Fix32.FromInt(2) + Half, Fix32.FromInt(2) + Half));

        return new ZoneDefinitions(ImmutableArray.Create(zone));
    }

    private static ZoneAabb TileObstacle(int x, int y)
        => new(new Vec2Fix(Fix32.FromInt(x) + Half, Fix32.FromInt(y) + Half), new Vec2Fix(Half, Half));

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 12,
        MapHeight: 12,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(32),
        Invariants: InvariantOptions.Enabled);
}

public sealed class ReplayVerifyNpcChaseCanaryTests
{
    private static readonly Fix32 Half = new(Fix32.OneRaw / 2);

    [Fact]
    [Trait("Category", "PR70")]
    [Trait("Category", "ReplayVerify")]
    [Trait("Category", "Canary")]
    public void ReplayVerify_NpcChase_Canary_IsStable()
    {
        ImmutableArray<string> baseline = RunReplay();
        ImmutableArray<string> replay = RunReplay();

        Assert.Equal(baseline.Length, replay.Length);
        for (int i = 0; i < baseline.Length; i++)
        {
            Assert.Equal(baseline[i], replay[i]);
        }
    }

    private static ImmutableArray<string> RunReplay()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = Simulation.CreateInitialState(config, BuildChaseZone());
        ZoneId zoneId = new(1);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(3), zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(8) + Half, Fix32.FromInt(8) + Half)))));

        ImmutableArray<string>.Builder checksums = ImmutableArray.CreateBuilder<string>(120);
        for (int tick = 0; tick < 120; tick++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
            checksums.Add(StateChecksum.ComputeGlobalChecksum(state));
        }

        return checksums.MoveToImmutable();
    }

    private static ZoneDefinitions BuildChaseZone()
    {
        ImmutableArray<ZoneAabb>.Builder obstacles = ImmutableArray.CreateBuilder<ZoneAabb>();
        for (int y = 1; y <= 9; y++)
        {
            if (y != 5)
            {
                obstacles.Add(new ZoneAabb(new Vec2Fix(Fix32.FromInt(5) + Half, Fix32.FromInt(y) + Half), new Vec2Fix(Half, Half)));
            }
        }

        ZoneDefinition zone = new(
            ZoneId: new ZoneId(1),
            Bounds: new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(11), Fix32.FromInt(11)),
            StaticObstacles: obstacles.MoveToImmutable(),
            NpcSpawns: ImmutableArray.Create(new NpcSpawnDefinition("npc.default", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(2) + Half, Fix32.FromInt(2) + Half)))),
            LootRules: null,
            RespawnPoint: new Vec2Fix(Fix32.FromInt(2) + Half, Fix32.FromInt(2) + Half));

        return new ZoneDefinitions(ImmutableArray.Create(zone));
    }

    private static SimulationConfig CreateConfig() => new(
        Seed: 7003,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 12,
        MapHeight: 12,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(32),
        Invariants: InvariantOptions.Enabled);
}
