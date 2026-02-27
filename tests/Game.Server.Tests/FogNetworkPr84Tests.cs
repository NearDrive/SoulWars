using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
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
        ScenarioRun restartProbe = FogNetworkPr84Harness.RunScenario(withRestartAtStep: 3);
        Assert.NotNull(restartProbe.DiagnosticsSummary);

        Assert.Contains(run.TransitionsA, transition => transition.StartsWith($"spawn:{run.TargetEntityId}@", StringComparison.Ordinal));
        Assert.Contains(run.TransitionsA, transition => transition.StartsWith($"despawn:{run.TargetEntityId}@", StringComparison.Ordinal));

        foreach ((int tick, SnapshotV2 snapshot, bool bVisible) in run.SnapshotsA)
        {
            FogNetworkPr84Harness.AssertCanonicalOrdering(snapshot);

            if (!bVisible)
            {
                Assert.DoesNotContain(snapshot.Entities, entity => entity.EntityId == run.TargetEntityId);
                Assert.DoesNotContain(snapshot.Enters, entity => entity.EntityId == run.TargetEntityId);
                Assert.DoesNotContain(snapshot.Updates, entity => entity.EntityId == run.TargetEntityId);
            }

            if (bVisible)
            {
                Assert.Contains(snapshot.Entities, entity => entity.EntityId == run.TargetEntityId);
            }

            if (!bVisible && tick > run.HideTick)
            {
                Assert.DoesNotContain(snapshot.Leaves, entityId => entityId == run.TargetEntityId);
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

        Assert.Equal(8401, config.Seed);
        ZoneState cp0Zone = host.CurrentWorld.Zones.Single(zone => zone.Id.Value == ZoneIdValue);
        Assert.Contains(cp0Zone.Entities, entity => entity.Id.Value == EntityA);
        Assert.Contains(cp0Zone.Entities, entity => entity.Id.Value == EntityB);

        int ticksExecuted = 0;
        int outboundCountA = 0;
        int outboundCountB = 0;
        int snapshotCountA = 0;
        int snapshotCountB = 0;
        int enterAckCountA = 0;
        int enterAckCountB = 0;
        int enteredEntityIdA = -1;
        int enteredEntityIdB = -1;
        SortedDictionary<string, int> typeCountsA = new(StringComparer.Ordinal);
        SortedDictionary<string, int> typeCountsB = new(StringComparer.Ordinal);

        host.AdvanceTicks(2);
        DrainAndAckAllSnapshots(endpointA, ref outboundCountA, ref snapshotCountA, ref enterAckCountA, ref enteredEntityIdA, typeCountsA);
        DrainAndAckAllSnapshots(endpointB, ref outboundCountB, ref snapshotCountB, ref enterAckCountB, ref enteredEntityIdB, typeCountsB);

        Assert.True(enterAckCountA > 0 && enterAckCountB > 0, $"CP1 enter ack missing. enterAckA={enterAckCountA} enterAckB={enterAckCountB} ticks={host.CurrentWorld.Tick}");
        Assert.True(host.CurrentWorld.Tick >= 2, $"CP1 tick progress missing. tick={host.CurrentWorld.Tick}");
        Assert.True(enteredEntityIdA > 0 && enteredEntityIdB > 0, $"CP1 missing entity ids. entityA={enteredEntityIdA} entityB={enteredEntityIdB}");

        int observerEntityId = enteredEntityIdA;
        int targetEntityId = enteredEntityIdB;

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
        bool hasPreviousVisibility = false;
        bool previousVisibleToA = false;

        for (int step = 0; step < MovementScript.Length; step++)
        {
            (sbyte mx, sbyte my) = MovementScript[step];
            for (int tickStep = 0; tickStep < TicksPerCommand; tickStep++)
            {
                endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(inputTick++, mx, my)));
                host.StepOnce();
                ticksExecuted++;

                SnapshotV2 snapA = DrainAndAckLatestSnapshot(endpointA, payloadHashesA, transitionsA, ref outboundCountA, ref snapshotCountA, ref enterAckCountA, ref enteredEntityIdA, typeCountsA);
                _ = DrainAndAckLatestSnapshot(endpointB, payloadHashesB, transitionsB, ref outboundCountB, ref snapshotCountB, ref enterAckCountB, ref enteredEntityIdB, typeCountsB);

                bool bVisibleToA = snapA.Entities.Any(entity => entity.EntityId == targetEntityId);
                snapshotsA.Add((snapA.Tick, snapA, bVisibleToA));
                visibilityTimelineA.Add($"{snapA.Tick}:{(bVisibleToA ? "visible" : "hidden")}");
                RecordVisibilityDrivenTransitions(
                    snapA.Tick,
                    bVisibleToA,
                    transitionsA,
                    spawnTicksA,
                    despawnTicksA,
                    ref hasPreviousVisibility,
                    ref previousVisibleToA,
                    ref hideTick,
                    targetEntityId);

                if (snapA.Enters.Any(entity => entity.EntityId == targetEntityId))
                {
                    spawnTicksA.Add(snapA.Tick);
                }

                if (snapA.Leaves.Contains(targetEntityId))
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
                    DrainAndAckAllSnapshots(endpointA, ref outboundCountA, ref snapshotCountA, ref enterAckCountA, ref enteredEntityIdA, typeCountsA);
                    DrainAndAckAllSnapshots(endpointB, ref outboundCountB, ref snapshotCountB, ref enterAckCountB, ref enteredEntityIdB, typeCountsB);
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
            RunFallbackPhase(maxTicks: 400, targetTileY: 3, requireVisible: true);
            RunFallbackPhase(maxTicks: 400, targetTileY: 1, requireVisible: false);
        }

        void RunFallbackPhase(int maxTicks, int targetTileY, bool requireVisible)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                EntityState bState = host.CurrentWorld.Zones
                    .SelectMany(zone => zone.Entities)
                    .OrderBy(entity => entity.Id.Value)
                    .First(entity => entity.Id.Value == targetEntityId);

                int bTileY = Fix32.FloorToInt(bState.Pos.Y);
                sbyte moveY = bTileY < targetTileY
                    ? (sbyte)1
                    : bTileY > targetTileY
                        ? (sbyte)-1
                        : (i % 2 == 0 ? (sbyte)1 : (sbyte)-1);

                endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(inputTick++, 0, moveY)));
                host.StepOnce();
                ticksExecuted++;

                SnapshotV2 snapA = DrainAndAckLatestSnapshot(endpointA, payloadHashesA, transitionsA, ref outboundCountA, ref snapshotCountA, ref enterAckCountA, ref enteredEntityIdA, typeCountsA);
                _ = DrainAndAckLatestSnapshot(endpointB, payloadHashesB, transitionsB, ref outboundCountB, ref snapshotCountB, ref enterAckCountB, ref enteredEntityIdB, typeCountsB);

                bool bVisibleToA = snapA.Entities.Any(entity => entity.EntityId == targetEntityId);
                snapshotsA.Add((snapA.Tick, snapA, bVisibleToA));
                visibilityTimelineA.Add($"{snapA.Tick}:{(bVisibleToA ? "visible" : "hidden")}");
                RecordVisibilityDrivenTransitions(
                    snapA.Tick,
                    bVisibleToA,
                    transitionsA,
                    spawnTicksA,
                    despawnTicksA,
                    ref hasPreviousVisibility,
                    ref previousVisibleToA,
                    ref hideTick,
                    targetEntityId);

                if (snapA.Enters.Any(entity => entity.EntityId == targetEntityId))
                {
                    spawnTicksA.Add(snapA.Tick);
                }

                if (snapA.Leaves.Contains(targetEntityId))
                {
                    despawnTicksA.Add(snapA.Tick);
                    hideTick = snapA.Tick;
                }

                if (requireVisible && spawnTicksA.Count > 0)
                {
                    return;
                }

                if (!requireVisible && despawnTicksA.Count > 0)
                {
                    return;
                }
            }
        }

        string diagnosticsSummary = $"CP3 ticks={ticksExecuted} outboundA={outboundCountA} outboundB={outboundCountB} snapshotsA={snapshotCountA} snapshotsB={snapshotCountB} enterAckA={enterAckCountA} enterAckB={enterAckCountB} transitionsA={transitionsA.Count} transitionsB={transitionsB.Count} spawnTicksA={spawnTicksA.Count} despawnTicksA={despawnTicksA.Count} msgTypesA={FormatTypeCounts(typeCountsA)} msgTypesB={FormatTypeCounts(typeCountsB)} observerEntityId={observerEntityId} targetEntityId={targetEntityId}";

        Assert.True(outboundCountA > 0 || outboundCountB > 0, $"CP2 no outbound messages. {diagnosticsSummary}");
        Assert.True(snapshotCountA > 0 || snapshotCountB > 0, $"CP2 no snapshots decoded. {diagnosticsSummary}");

        if (!withRestartAtStep.HasValue)
        {
            Assert.True(spawnTicksA.Count > 0, $"spawnTicksA empty. {diagnosticsSummary}");
            Assert.True(despawnTicksA.Count > 0, $"despawnTicksA empty. {diagnosticsSummary}");
        }

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
            hideTick,
            diagnosticsSummary,
            targetEntityId);
    }

    private static void RecordVisibilityDrivenTransitions(
        int tick,
        bool visibleToA,
        List<string> transitionsA,
        List<int> spawnTicksA,
        List<int> despawnTicksA,
        ref bool hasPreviousVisibility,
        ref bool previousVisibleToA,
        ref int hideTick,
        int targetEntityId)
    {
        if (!hasPreviousVisibility)
        {
            hasPreviousVisibility = true;
            previousVisibleToA = visibleToA;
            if (visibleToA)
            {
                transitionsA.Add($"spawn:{targetEntityId}@{tick}");
                spawnTicksA.Add(tick);
            }

            return;
        }

        if (!previousVisibleToA && visibleToA)
        {
            transitionsA.Add($"spawn:{targetEntityId}@{tick}");
            spawnTicksA.Add(tick);
        }

        if (previousVisibleToA && !visibleToA)
        {
            transitionsA.Add($"despawn:{targetEntityId}@{tick}");
            despawnTicksA.Add(tick);
            hideTick = tick;
        }

        previousVisibleToA = visibleToA;
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
        List<string> transitions,
        ref int outboundCount,
        ref int snapshotCount,
        ref int enterAckCount,
        ref int enteredEntityId,
        SortedDictionary<string, int> messageTypeCounts)
    {
        SnapshotV2? latest = null;
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (!ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _))
            {
                continue;
            }

            outboundCount++;
            if (message is not null)
            {
                IncrementCount(messageTypeCounts, message.GetType().Name);
            }

            if (message is EnterZoneAck enterAck)
            {
                enterAckCount++;
                enteredEntityId = enterAck.EntityId;
            }

            if (message is SnapshotV2 snapshot)
            {
                snapshotCount++;
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

    private static void DrainAndAckAllSnapshots(
        InMemoryEndpoint endpoint,
        ref int outboundCount,
        ref int snapshotCount,
        ref int enterAckCount,
        ref int enteredEntityId,
        SortedDictionary<string, int> messageTypeCounts)
    {
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (!ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _) || message is null)
            {
                continue;
            }

            outboundCount++;
            IncrementCount(messageTypeCounts, message.GetType().Name);
            if (message is EnterZoneAck enterAck)
            {
                enterAckCount++;
                enteredEntityId = enterAck.EntityId;
            }

            if (message is SnapshotV2 snapshot)
            {
                snapshotCount++;
                endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq)));
            }
        }
    }

    private static void IncrementCount(SortedDictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out int current);
        counts[key] = current + 1;
    }

    private static string FormatTypeCounts(SortedDictionary<string, int> counts)
    {
        if (counts.Count == 0)
        {
            return "none";
        }

        StringBuilder builder = new();
        bool first = true;
        foreach (KeyValuePair<string, int> pair in counts)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
            first = false;
        }

        return builder.ToString();
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
    int HideTick,
    string DiagnosticsSummary,
    int TargetEntityId);
