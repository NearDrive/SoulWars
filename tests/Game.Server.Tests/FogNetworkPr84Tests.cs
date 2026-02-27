using System.Collections.Immutable;
using System.Security.Cryptography;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "PR84")]
public sealed class FogNetworkReplayVerifyTests
{
    [Fact]
    [Trait("Category", "ReplayVerify")]
    public void ReplayVerify_FogNetwork_NoInvisibleEntityLeak_AndDeterministicTransitions()
    {
        ScenarioRun run = FogNetworkPr84Harness.RunScenario(withRestartAtStep: null);

        Assert.Contains(run.TransitionsA, transition => transition.StartsWith("spawn:21@", StringComparison.Ordinal));
        Assert.Contains(run.TransitionsA, transition => transition.StartsWith("despawn:21@", StringComparison.Ordinal));

        foreach ((int tick, SnapshotV2 snapshot, bool bVisible) in run.SnapshotsA)
        {
            FogNetworkPr84Harness.AssertCanonicalOrdering(snapshot);

            if (!bVisible)
            {
                Assert.DoesNotContain(snapshot.Entities, entity => entity.EntityId == 21);
                Assert.DoesNotContain(snapshot.Enters, entity => entity.EntityId == 21);
                Assert.DoesNotContain(snapshot.Updates, entity => entity.EntityId == 21);
            }

            if (bVisible)
            {
                Assert.Contains(snapshot.Entities, entity => entity.EntityId == 21);
            }

            if (!bVisible && tick > run.HideTick)
            {
                Assert.DoesNotContain(snapshot.Leaves, entityId => entityId == 21);
            }
        }

        Assert.Equal(
            run.PayloadHashesA.OrderBy(hash => hash.Key).Select(hash => hash.Value),
            run.PayloadHashesA.Select(hash => hash.Value));
    }
}

[Trait("Category", "PR84")]
public sealed class FogNetworkRestartDeterminismTests
{
    [Fact]
    [Trait("Category", "Canary")]
    public void Canary_FogNetwork_RestartMidTransition_IsDeterministic()
    {
        const int restartAtStep = 3;
        ScenarioRun runA = FogNetworkPr84Harness.RunScenario(withRestartAtStep: restartAtStep);
        ScenarioRun runB = FogNetworkPr84Harness.RunScenario(withRestartAtStep: restartAtStep);

        Assert.Equal(runA.PayloadHashChecksumA, runB.PayloadHashChecksumA);
        Assert.Equal(runA.PayloadHashChecksumB, runB.PayloadHashChecksumB);
        Assert.Equal(runA.TransitionsA, runB.TransitionsA);
        Assert.Equal(runA.TransitionsB, runB.TransitionsB);
        Assert.Equal(runA.VisibilityTimelineA, runB.VisibilityTimelineA);
        Assert.Equal(runA.SpawnTicksA, runB.SpawnTicksA);
        Assert.Equal(runA.DespawnTicksA, runB.DespawnTicksA);
    }
}

file static class FogNetworkPr84Harness
{
    private const int ZoneIdValue = 1;
    private const int EntityA = 11;
    private const int EntityB = 21;

    private const int TicksPerCommand = 20;

    private static readonly ImmutableArray<(sbyte MoveX, sbyte MoveY)> MovementScript =
    [
        (0, 1),
        (0, 1),
        (0, 0),
        (0, 0),
        (0, -1),
        (0, -1),
        (0, 0),
        (0, 0)
    ];

    public static ScenarioRun RunScenario(int? withRestartAtStep)
    {
        ServerConfig config = ServerConfig.Default(seed: 8401) with
        {
            SnapshotEveryTicks = 1,
            VisionRadius = Fix32.FromInt(8),
            VisionRadiusSq = Fix32.FromInt(64),
            NpcCountPerZone = 0
        };

        ServerHost host = CreateHost(config);
        InMemoryEndpoint endpointA = new();
        InMemoryEndpoint endpointB = new();

        host.Connect(endpointA);
        host.Connect(endpointB);
        HandshakeAndEnter(endpointA, "pr84-a");
        HandshakeAndEnter(endpointB, "pr84-b");

        host.AdvanceTicks(2);
        DrainAndAckAllSnapshots(endpointA);
        DrainAndAckAllSnapshots(endpointB);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ZoneIdValue, 0)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ZoneIdValue, 0)));

        int inputTick = host.CurrentWorld.Tick + 1;
        int hideTick = -1;
        List<(int Tick, SnapshotV2 Snapshot, bool BVisible)> snapshotsA = new();
        List<string> transitionsA = new();
        List<string> transitionsB = new();
        List<int> spawnTicksA = new();
        List<int> despawnTicksA = new();
        List<KeyValuePair<int, string>> payloadHashesA = new();
        List<KeyValuePair<int, string>> payloadHashesB = new();
        List<string> visibilityTimelineA = new();

        for (int step = 0; step < MovementScript.Length; step++)
        {
            (sbyte mx, sbyte my) = MovementScript[step];
            for (int tickStep = 0; tickStep < TicksPerCommand; tickStep++)
            {
                endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(inputTick++, mx, my)));
                host.StepOnce();

                SnapshotV2 snapA = DrainAndAckLatestSnapshot(endpointA, payloadHashesA, transitionsA);
                _ = DrainAndAckLatestSnapshot(endpointB, payloadHashesB, transitionsB);

                bool bVisibleToA = snapA.Entities.Any(entity => entity.EntityId == EntityB);
                snapshotsA.Add((snapA.Tick, snapA, bVisibleToA));
                visibilityTimelineA.Add($"{snapA.Tick}:{(bVisibleToA ? "visible" : "hidden")}");

                if (snapA.Enters.Any(entity => entity.EntityId == EntityB))
                {
                    spawnTicksA.Add(snapA.Tick);
                }

                if (snapA.Leaves.Contains(EntityB))
                {
                    despawnTicksA.Add(snapA.Tick);
                    hideTick = snapA.Tick;
                }
            }

            if (withRestartAtStep.HasValue && step == withRestartAtStep.Value)
            {
                string dbPath = Path.Combine(Path.GetTempPath(), $"soulwars-pr84-{Guid.NewGuid():N}.db");
                try
                {
                    host.SaveToSqlite(dbPath);
                    host = ServerHost.LoadFromSqlite(config, dbPath);

                    endpointA = new InMemoryEndpoint();
                    endpointB = new InMemoryEndpoint();
                    host.Connect(endpointA);
                    host.Connect(endpointB);

                    HandshakeAndEnter(endpointA, "pr84-a");
                    HandshakeAndEnter(endpointB, "pr84-b");
                    endpointA.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ZoneIdValue, 0)));
                    endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ZoneIdValue, 0)));

                    host.AdvanceTicks(2);
                    DrainAndAckAllSnapshots(endpointA);
                    DrainAndAckAllSnapshots(endpointB);
                    inputTick = host.CurrentWorld.Tick + 1;
                }
                finally
                {
                    if (File.Exists(dbPath))
                    {
                        File.Delete(dbPath);
                    }
                }
            }
        }


        if (spawnTicksA.Count == 0 || despawnTicksA.Count == 0)
        {
            ImmutableArray<(sbyte MoveX, sbyte MoveY)> fallbackScript =
            [
                (0, 1),
                (0, 1),
                (0, 0),
                (0, -1),
                (0, -1),
                (0, 0)
            ];

            for (int i = 0; i < fallbackScript.Length; i++)
            {
                (sbyte fmx, sbyte fmy) = fallbackScript[i];
                for (int tickStep = 0; tickStep < TicksPerCommand; tickStep++)
                {
                    endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(inputTick++, fmx, fmy)));
                    host.StepOnce();

                    SnapshotV2 snapA = DrainAndAckLatestSnapshot(endpointA, payloadHashesA, transitionsA);
                    _ = DrainAndAckLatestSnapshot(endpointB, payloadHashesB, transitionsB);

                    bool bVisibleToA = snapA.Entities.Any(entity => entity.EntityId == EntityB);
                    snapshotsA.Add((snapA.Tick, snapA, bVisibleToA));
                    visibilityTimelineA.Add($"{snapA.Tick}:{(bVisibleToA ? "visible" : "hidden")}");

                    if (snapA.Enters.Any(entity => entity.EntityId == EntityB))
                    {
                        spawnTicksA.Add(snapA.Tick);
                    }

                    if (snapA.Leaves.Contains(EntityB))
                    {
                        despawnTicksA.Add(snapA.Tick);
                        hideTick = snapA.Tick;
                    }
                }

                if (spawnTicksA.Count > 0 && despawnTicksA.Count > 0)
                {
                    break;
                }
            }
        }

        Assert.NotEmpty(spawnTicksA);
        Assert.NotEmpty(despawnTicksA);

        return new ScenarioRun(
            snapshotsA,
            transitionsA,
            transitionsB,
            payloadHashesA,
            payloadHashesB,
            ComputeChecksum(payloadHashesA.Select(pair => pair.Value)),
            ComputeChecksum(payloadHashesB.Select(pair => pair.Value)),
            visibilityTimelineA,
            spawnTicksA,
            despawnTicksA,
            hideTick);
    }

    public static void AssertCanonicalOrdering(SnapshotV2 snapshot)
    {
        Assert.Equal(snapshot.Entities.Select(entity => entity.EntityId).OrderBy(id => id), snapshot.Entities.Select(entity => entity.EntityId));
        Assert.Equal(snapshot.Enters.Select(entity => entity.EntityId).OrderBy(id => id), snapshot.Enters.Select(entity => entity.EntityId));
        Assert.Equal(snapshot.Updates.Select(entity => entity.EntityId).OrderBy(id => id), snapshot.Updates.Select(entity => entity.EntityId));
        Assert.Equal(snapshot.Leaves.OrderBy(id => id), snapshot.Leaves);
    }

    private static string ComputeChecksum(IEnumerable<string> values)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string value in values)
        {
            hash.AppendData(Convert.FromHexString(value));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static SnapshotV2 DrainAndAckLatestSnapshot(
        InMemoryEndpoint endpoint,
        List<KeyValuePair<int, string>> payloadHashes,
        List<string> transitions)
    {
        SnapshotV2? latest = null;
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (!ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _))
            {
                continue;
            }

            if (message is SnapshotV2 snapshot)
            {
                string payloadHash = Convert.ToHexString(SHA256.HashData(payload));
                payloadHashes.Add(new KeyValuePair<int, string>(snapshot.Tick, payloadHash));
                transitions.AddRange(snapshot.Enters.Select(entity => $"spawn:{entity.EntityId}@{snapshot.Tick}"));
                transitions.AddRange(snapshot.Leaves.Select(entityId => $"despawn:{entityId}@{snapshot.Tick}"));
                endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq)));
                latest = snapshot;
            }
        }

        Assert.NotNull(latest);
        return latest!;
    }

    private static void DrainAndAckAllSnapshots(InMemoryEndpoint endpoint)
    {
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _) && message is SnapshotV2 snapshot)
            {
                endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq)));
            }
        }
    }

    private static ServerHost CreateHost(ServerConfig config)
    {
        TileMap map = CreateObstacleMap();
        ImmutableArray<EntityState> entities =
        [
            new EntityState(new EntityId(EntityA), At(2, 3), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 8),
            new EntityState(new EntityId(EntityB), At(6, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 8)
        ];

        ZoneState zone = new(new ZoneId(ZoneIdValue), map, entities);
        ImmutableArray<EntityLocation> locations = entities
            .OrderBy(entity => entity.Id.Value)
            .Select(entity => new EntityLocation(entity.Id, zone.Id))
            .ToImmutableArray();

        WorldState world = new(0, ImmutableArray.Create(zone), locations);
        ServerBootstrap bootstrap = new(
            world,
            config.Seed,
            ImmutableArray.Create(
                new BootstrapPlayerRecord("pr84-a", 8401001, EntityA, ZoneIdValue),
                new BootstrapPlayerRecord("pr84-b", 8401002, EntityB, ZoneIdValue)));

        return new ServerHost(config, bootstrap: bootstrap);
    }

    private static TileMap CreateObstacleMap()
    {
        const int width = 10;
        const int height = 8;
        TileKind[] tiles = Enumerable.Repeat(TileKind.Empty, width * height).ToArray();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    tiles[(y * width) + x] = TileKind.Solid;
                }
            }
        }

        for (int y = 1; y <= 6; y++)
        {
            if (y == 3)
            {
                continue;
            }

            tiles[(y * width) + 4] = TileKind.Solid;
        }

        return new TileMap(width, height, tiles.ToImmutableArray());
    }

    private static Vec2Fix At(int x, int y)
        => new(Fix32.FromInt(x), Fix32.FromInt(y));

    private static void HandshakeAndEnter(InMemoryEndpoint endpoint, string accountId)
    {
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, accountId)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(ZoneIdValue)));
    }
}

public sealed record ScenarioRun(
    List<(int Tick, SnapshotV2 Snapshot, bool BVisible)> SnapshotsA,
    List<string> TransitionsA,
    List<string> TransitionsB,
    List<KeyValuePair<int, string>> PayloadHashesA,
    List<KeyValuePair<int, string>> PayloadHashesB,
    string PayloadHashChecksumA,
    string PayloadHashChecksumB,
    List<string> VisibilityTimelineA,
    List<int> SpawnTicksA,
    List<int> DespawnTicksA,
    int HideTick);
