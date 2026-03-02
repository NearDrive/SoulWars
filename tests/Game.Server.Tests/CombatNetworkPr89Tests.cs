using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Game.Core;
using Game.Protocol;
using ProtocolCastSkillCommand = Game.Protocol.CastSkillCommand;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "PR89")]
public sealed class CombatNetworkCanaryTests
{
    [Fact]
    [Trait("Category", "PR89")]
    [Trait("Category", "Canary")]
    public void Canary_CombatNetwork_VisibilityTransitions_AndHitTiming_AreStable()
    {
        CombatScenarioRun run = CombatNetworkPr89Harness.RunScenario(restartAfterStep: null);

        ServerInvariants.ValidateVisibilityStreamInvariants(
            run.ObserverSnapshots,
            tick => run.ExpectedVisibleByTick[tick],
            run.ObserverSessionId);

        Assert.True(run.SpawnTicks.Count == 1,
            $"Expected exactly one spawn transition. spawnTicks=[{string.Join(',', run.SpawnTicks)}] summary={run.DiagnosticsSummary}");
        Assert.True(run.DespawnTicks.Count == 1,
            $"Expected exactly one despawn transition. despawnTicks=[{string.Join(',', run.DespawnTicks)}] summary={run.DiagnosticsSummary}");
        int spawnTick = Assert.Single(run.SpawnTicks);
        int despawnTick = Assert.Single(run.DespawnTicks);
        Assert.True(spawnTick < despawnTick);

        int targetEntityId = run.TargetEntityId;
        foreach (SnapshotV2 snapshot in run.ObserverSnapshots)
        {
            bool visible = run.ExpectedVisibleByTick[snapshot.Tick].Contains(targetEntityId);
            if (!visible)
            {
                Assert.DoesNotContain(snapshot.Entities, entity => entity.EntityId == targetEntityId);
                Assert.DoesNotContain(snapshot.Enters, entity => entity.EntityId == targetEntityId);
                Assert.DoesNotContain(snapshot.Updates, entity => entity.EntityId == targetEntityId);
            }
        }

        HitEventV1[] hits = CombatNetworkPr89Harness.ExtractMatchingHits(
            run.ObserverSnapshots,
            run.ObserverEntityId,
            run.TargetEntityId);

        Assert.True(hits.Length > 0, $"Expected at least one matching hit event. summary={run.DiagnosticsSummary}");
        HitEventV1 hit = hits
            .OrderBy(h => h.TickId)
            .ThenBy(h => h.SourceEntityId)
            .ThenBy(h => h.TargetEntityId)
            .ThenBy(h => h.AbilityId)
            .ThenBy(h => h.EventSeq)
            .First();
        Assert.True(hits.Length == 1, $"Expected exactly one matching hit event. hitCount={hits.Length} summary={run.DiagnosticsSummary}");
        Assert.True(hit.TickId >= run.CastTick);
        Assert.True(run.ExpectedVisibleByTick[hit.TickId].Contains(run.TargetEntityId));

        SnapshotV2 hitTickSnapshot = run.ObserverSnapshots.Single(snapshot => snapshot.Tick == hit.TickId);
        SnapshotEntity targetAtHit = Assert.Single(hitTickSnapshot.Entities, entity => entity.EntityId == run.TargetEntityId);
        long dx = (long)targetAtHit.PosXRaw - hit.HitPosXRaw;
        long dy = (long)targetAtHit.PosYRaw - hit.HitPosYRaw;
        long distanceSq = dx * dx + dy * dy;
        long radiusSq = (long)Fix32.OneRaw * Fix32.OneRaw;
        Assert.True(distanceSq <= radiusSq, $"HitEvent outside radius. distSq={distanceSq} radiusSq={radiusSq} tick={hit.TickId}");

        int hitsBeforeCast = run.ObserverSnapshots
            .Where(snapshot => snapshot.Tick < run.CastTick)
            .SelectMany(snapshot => snapshot.HitEvents)
            .Count(hitEvent => hitEvent.TargetEntityId == run.TargetEntityId);
        Assert.Equal(0, hitsBeforeCast);
    }
}

[Trait("Category", "PR89")]
public sealed class CombatReplayVerifyScenario
{
    [Fact]
    [Trait("Category", "PR89")]
    [Trait("Category", "Canary")]
    [Trait("Category", "ReplayVerify")]
    public void ReplayVerify_CombatNetworkScenario_RestartMidFight_IsDeterministic()
    {
        CombatScenarioRun baselineA = CombatNetworkPr89Harness.RunScenario(restartAfterStep: null);
        CombatScenarioRun baselineB = CombatNetworkPr89Harness.RunScenario(restartAfterStep: null);

        Assert.Equal(TestChecksum.NormalizeFullHex(baselineA.FinalChecksum), TestChecksum.NormalizeFullHex(baselineB.FinalChecksum));
        Assert.Equal(baselineA.ObserverPayloadHashes, baselineB.ObserverPayloadHashes);

        CombatScenarioRun restartA = CombatNetworkPr89Harness.RunScenario(restartAfterStep: 3);
        CombatScenarioRun restartB = CombatNetworkPr89Harness.RunScenario(restartAfterStep: 3);

        Assert.Equal(TestChecksum.NormalizeFullHex(restartA.FinalChecksum), TestChecksum.NormalizeFullHex(restartB.FinalChecksum));
        Assert.Equal(restartA.ObserverPayloadHashes, restartB.ObserverPayloadHashes);
        Assert.Equal(TestChecksum.NormalizeFullHex(baselineA.FinalChecksum), TestChecksum.NormalizeFullHex(restartA.FinalChecksum));
    }
}

file static class CombatNetworkPr89Harness
{
    private const int ZoneIdValue = 1;
    private const int ObserverEntityId = 51;
    private const int TargetEntityId = 61;
    private const int TicksPerStep = 10;

    private static readonly ImmutableArray<StepCommand> Script =
    [
        new StepCommand(0, 1, 0, 0, CastPoint: false),
        new StepCommand(0, 1, 0, 0, CastPoint: false),
        new StepCommand(0, 0, 0, 0, CastPoint: false),
        new StepCommand(0, 0, 0, 0, CastPoint: false),
        new StepCommand(0, 0, 0, 0, CastPoint: false),
        new StepCommand(0, -1, 0, 0, CastPoint: false),
        new StepCommand(0, -1, 0, 0, CastPoint: false),
        new StepCommand(0, 0, 0, 0, CastPoint: false),
    ];

    public static CombatScenarioRun RunScenario(int? restartAfterStep)
    {
        ServerConfig config = ServerConfig.Default(seed: 8901) with
        {
            SnapshotEveryTicks = 1,
            NpcCountPerZone = 0,
            VisionRadius = Fix32.FromInt(8),
            VisionRadiusSq = Fix32.FromInt(64)
        };

        ServerHost host = CreateHost(config);
        InMemoryEndpoint observerEndpoint = new();
        InMemoryEndpoint targetEndpoint = new();

        host.Connect(observerEndpoint);
        host.Connect(targetEndpoint);
        HandshakeAndEnter(observerEndpoint, "pr89-observer");
        HandshakeAndEnter(targetEndpoint, "pr89-target");

        int observerSessionId = -1;
        int observerRuntimeEntityId = -1;
        int targetSessionId = -1;
        int targetRuntimeEntityId = -1;

        host.AdvanceTicks(2);
        _ = DrainAndAckLatestSnapshotOrNull(observerEndpoint, ref observerSessionId, ref observerRuntimeEntityId, []);
        _ = DrainAndAckLatestSnapshotOrNull(targetEndpoint, ref targetSessionId, ref targetRuntimeEntityId, []);

        Assert.Equal(ObserverEntityId, observerRuntimeEntityId);
        Assert.Equal(TargetEntityId, targetRuntimeEntityId);

        observerEndpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ZoneIdValue, 0)));
        targetEndpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ZoneIdValue, 0)));

        List<string> payloadHashes = new();
        _ = AwaitSnapshot(host, observerEndpoint, ref observerSessionId, ref observerRuntimeEntityId, payloadHashes);
        _ = AwaitSnapshot(host, targetEndpoint, ref targetSessionId, ref targetRuntimeEntityId, []);

        VisibilityAoiProvider aoiProvider = new();
        List<SnapshotV2> observerSnapshots = new();
        Dictionary<int, IReadOnlySet<int>> expectedVisibleByTick = new();
        List<int> spawnTicks = new();
        List<int> despawnTicks = new();

        int castTick = -1;
        int? firstSpawnTick = null;
        bool castSent = false;
        int? firstHitTick = null;
        int inputTick = host.CurrentWorld.Tick + 1;
        bool previousVisibleToObserver = false;

        bool scenarioComplete = false;
        for (int step = 0; step < Script.Length && !scenarioComplete; step++)
        {
            StepCommand command = Script[step];
            for (int tickInStep = 0; tickInStep < TicksPerStep && !scenarioComplete; tickInStep++)
            {
                observerEndpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(inputTick, command.ObserverMoveX, command.ObserverMoveY)));
                targetEndpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(inputTick, command.TargetMoveX, command.TargetMoveY)));

                bool shouldCast = !castSent && firstSpawnTick.HasValue && previousVisibleToObserver && inputTick >= firstSpawnTick.Value + 2;
                if (shouldCast)
                {
                    Assert.True(previousVisibleToObserver, $"Cast requires visible target. tick={inputTick}");
                    ZoneState zone = host.CurrentWorld.Zones.Single(zone => zone.Id.Value == ZoneIdValue);
                    EntityState target = zone.Entities.Single(entity => entity.Id.Value == targetRuntimeEntityId);

                    observerEndpoint.EnqueueToServer(ProtocolCodec.Encode(new ProtocolCastSkillCommand(
                        Tick: inputTick,
                        CasterId: observerRuntimeEntityId,
                        SkillId: 1,
                        ZoneId: ZoneIdValue,
                        TargetKind: (byte)CastTargetKind.Point,
                        TargetEntityId: 0,
                        TargetPosXRaw: target.Pos.X.Raw,
                        TargetPosYRaw: target.Pos.Y.Raw)));

                    castTick = inputTick;
                    castSent = true;
                }

                inputTick++;
                host.StepOnce();

                SnapshotV2 observerSnapshot = DrainAndAckLatestSnapshot(observerEndpoint, ref observerSessionId, ref observerRuntimeEntityId, payloadHashes);
                _ = DrainAndAckLatestSnapshot(targetEndpoint, ref targetSessionId, ref targetRuntimeEntityId, []);
                observerSnapshots.Add(observerSnapshot);

                ImmutableHashSet<int> visible = aoiProvider.ComputeVisible(host.CurrentWorld, new ZoneId(ZoneIdValue), new EntityId(observerRuntimeEntityId)).EntityIds
                    .Select(entityId => entityId.Value)
                    .ToImmutableHashSet();
                expectedVisibleByTick[observerSnapshot.Tick] = visible;

                bool visibleToObserver = observerSnapshot.Entities.Any(entity => entity.EntityId == targetRuntimeEntityId);
                if (!previousVisibleToObserver && visibleToObserver)
                {
                    spawnTicks.Add(observerSnapshot.Tick);
                    firstSpawnTick ??= observerSnapshot.Tick;
                }

                if (previousVisibleToObserver && !visibleToObserver)
                {
                    despawnTicks.Add(observerSnapshot.Tick);
                }

                previousVisibleToObserver = visibleToObserver;

                HitEventV1[] matchingHits = observerSnapshot.HitEvents
                    .Where(hit => hit.SourceEntityId == observerRuntimeEntityId && hit.TargetEntityId == targetRuntimeEntityId)
                    .ToArray();
                if (matchingHits.Length > 0)
                {
                    firstHitTick ??= matchingHits.Min(hit => hit.TickId);
                }

                if (firstHitTick.HasValue && observerSnapshot.Tick >= firstHitTick.Value + 2)
                {
                    scenarioComplete = true;
                }
            }

            if (restartAfterStep.HasValue && restartAfterStep.Value == step)
            {
                host = CreateHost(config);
                observerEndpoint = new InMemoryEndpoint();
                targetEndpoint = new InMemoryEndpoint();
                host.Connect(observerEndpoint);
                host.Connect(targetEndpoint);

                HandshakeAndEnter(observerEndpoint, "pr89-observer");
                HandshakeAndEnter(targetEndpoint, "pr89-target");

                host.AdvanceTicks(2);
                _ = DrainAndAckLatestSnapshotOrNull(observerEndpoint, ref observerSessionId, ref observerRuntimeEntityId, payloadHashes);
                _ = DrainAndAckLatestSnapshotOrNull(targetEndpoint, ref targetSessionId, ref targetRuntimeEntityId, []);

                observerEndpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ZoneIdValue, 0)));
                targetEndpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ZoneIdValue, 0)));

                _ = AwaitSnapshot(host, observerEndpoint, ref observerSessionId, ref observerRuntimeEntityId, payloadHashes);
                _ = AwaitSnapshot(host, targetEndpoint, ref targetSessionId, ref targetRuntimeEntityId, []);

                inputTick = host.CurrentWorld.Tick + 1;

                int? replayFirstSpawnTick = null;
                bool replayPreviousVisible = false;
                bool replayCastSent = false;
                for (int replayStep = 0; replayStep <= step; replayStep++)
                {
                    StepCommand replayCommand = Script[replayStep];
                    for (int replayTickInStep = 0; replayTickInStep < TicksPerStep; replayTickInStep++)
                    {
                        observerEndpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(inputTick, replayCommand.ObserverMoveX, replayCommand.ObserverMoveY)));
                        targetEndpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(inputTick, replayCommand.TargetMoveX, replayCommand.TargetMoveY)));

                        bool shouldReplayCast = !replayCastSent && replayFirstSpawnTick.HasValue && replayPreviousVisible && inputTick >= replayFirstSpawnTick.Value + 2;
                        if (shouldReplayCast)
                        {
                            Assert.True(replayPreviousVisible, $"Replay cast requires visible target. tick={inputTick}");
                            ZoneState zone = host.CurrentWorld.Zones.Single(zone => zone.Id.Value == ZoneIdValue);
                            EntityState target = zone.Entities.Single(entity => entity.Id.Value == targetRuntimeEntityId);
                            observerEndpoint.EnqueueToServer(ProtocolCodec.Encode(new ProtocolCastSkillCommand(
                                Tick: inputTick,
                                CasterId: observerRuntimeEntityId,
                                SkillId: 1,
                                ZoneId: ZoneIdValue,
                                TargetKind: (byte)CastTargetKind.Point,
                                TargetEntityId: 0,
                                TargetPosXRaw: target.Pos.X.Raw,
                                TargetPosYRaw: target.Pos.Y.Raw)));
                            replayCastSent = true;
                        }

                        inputTick++;
                        host.StepOnce();
                        SnapshotV2 replayObserverSnapshot = DrainAndAckLatestSnapshot(observerEndpoint, ref observerSessionId, ref observerRuntimeEntityId, payloadHashes);
                        _ = DrainAndAckLatestSnapshot(targetEndpoint, ref targetSessionId, ref targetRuntimeEntityId, []);

                        bool replayVisible = replayObserverSnapshot.Entities.Any(entity => entity.EntityId == targetRuntimeEntityId);
                        if (!replayPreviousVisible && replayVisible)
                        {
                            replayFirstSpawnTick ??= replayObserverSnapshot.Tick;
                        }

                        replayPreviousVisible = replayVisible;
                    }
                }

                previousVisibleToObserver = false;
            }
        }

        Assert.True(castTick > 0, "Expected cast tick to be set.");

        return new CombatScenarioRun(
            observerSnapshots,
            expectedVisibleByTick,
            observerSessionId,
            observerRuntimeEntityId,
            targetRuntimeEntityId,
            castTick,
            spawnTicks,
            despawnTicks,
            payloadHashes,
            StateChecksum.Compute(host.CurrentWorld),
            BuildDiagnosticsSummary(observerSnapshots, spawnTicks, despawnTicks, castTick));
    }

    private static SnapshotV2 DrainAndAckLatestSnapshot(InMemoryEndpoint endpoint, ref int sessionId, ref int entityId, List<string> payloadHashes)
        => DrainAndAckLatestSnapshotOrNull(endpoint, ref sessionId, ref entityId, payloadHashes)
            ?? throw new Xunit.Sdk.XunitException("Expected snapshot.");

    private static SnapshotV2 AwaitSnapshot(ServerHost host, InMemoryEndpoint endpoint, ref int sessionId, ref int entityId, List<string> payloadHashes)
    {
        SnapshotV2? snapshot = DrainAndAckLatestSnapshotOrNull(endpoint, ref sessionId, ref entityId, payloadHashes);
        for (int i = 0; snapshot is null && i < 64; i++)
        {
            host.StepOnce();
            snapshot = DrainAndAckLatestSnapshotOrNull(endpoint, ref sessionId, ref entityId, payloadHashes);
        }

        return snapshot ?? throw new Xunit.Sdk.XunitException("Expected snapshot after bootstrap ticks.");
    }

    private static SnapshotV2? DrainAndAckLatestSnapshotOrNull(InMemoryEndpoint endpoint, ref int sessionId, ref int entityId, List<string> payloadHashes)
    {
        SnapshotV2? latestSnapshot = null;

        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (!ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _) || message is null)
            {
                continue;
            }

            if (message is Welcome welcome)
            {
                sessionId = welcome.SessionId.Value;
            }

            if (message is EnterZoneAck ack)
            {
                entityId = ack.EntityId;
            }

            if (message is SnapshotV2 snapshot)
            {
                latestSnapshot = snapshot;
                payloadHashes.Add($"{snapshot.Tick}:{ComputePayloadHash(payload)}");
                endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq)));
            }
        }

        return latestSnapshot;
    }



    public static HitEventV1[] ExtractMatchingHits(IEnumerable<SnapshotV2> snapshots, int sourceEntityId, int targetEntityId)
    {
        List<HitEventV1> fromHitEvents = snapshots
            .SelectMany(snapshot => snapshot.HitEvents)
            .Where(hit => hit.SourceEntityId == sourceEntityId && hit.TargetEntityId == targetEntityId)
            .OrderBy(h => h.TickId)
            .ThenBy(h => h.SourceEntityId)
            .ThenBy(h => h.TargetEntityId)
            .ThenBy(h => h.AbilityId)
            .ThenBy(h => h.EventSeq)
            .ToList();

        if (fromHitEvents.Count > 0)
        {
            return fromHitEvents.ToArray();
        }

        return snapshots
            .SelectMany(snapshot => snapshot.ProjectileEvents)
            .Where(evt => evt.Kind == (byte)ProjectileEventKind.Hit)
            .Where(evt => evt.SourceEntityId == sourceEntityId && evt.TargetEntityId == targetEntityId)
            .OrderBy(evt => evt.TickId)
            .ThenBy(evt => evt.SourceEntityId)
            .ThenBy(evt => evt.TargetEntityId)
            .ThenBy(evt => evt.AbilityId)
            .Select((evt, idx) => new HitEventV1(
                TickId: evt.TickId,
                ZoneId: evt.ZoneId,
                SourceEntityId: evt.SourceEntityId,
                TargetEntityId: evt.TargetEntityId,
                AbilityId: evt.AbilityId,
                HitPosXRaw: evt.PosXRaw,
                HitPosYRaw: evt.PosYRaw,
                EventSeq: idx))
            .ToArray();
    }
    private static string BuildDiagnosticsSummary(List<SnapshotV2> snapshots, List<int> spawnTicks, List<int> despawnTicks, int castTick)
    {
        Dictionary<int, int> hitCountsByTick = snapshots
            .SelectMany(s => s.HitEvents.Select(h => h.TickId))
            .GroupBy(tick => tick)
            .ToDictionary(g => g.Key, g => g.Count());

        Dictionary<int, int> projectileEventCountsByTick = snapshots
            .SelectMany(s => s.ProjectileEvents.Select(e => e.TickId))
            .GroupBy(tick => tick)
            .ToDictionary(g => g.Key, g => g.Count());

        int totalHitEvents = snapshots.Sum(snapshot => snapshot.HitEvents.Length);
        int totalProjectileEvents = snapshots.Sum(snapshot => snapshot.ProjectileEvents.Length);
        int totalProjectileHitEvents = snapshots.Sum(snapshot => snapshot.ProjectileEvents.Count(e => e.Kind == (byte)ProjectileEventKind.Hit));

        string hitSummary = string.Join(',', hitCountsByTick.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        string projectileSummary = string.Join(',', projectileEventCountsByTick.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        return $"ticks={snapshots.Count};castTick={castTick};spawns=[{string.Join(',', spawnTicks)}];despawns=[{string.Join(',', despawnTicks)}];totalHitEvents={totalHitEvents};totalProjectileEvents={totalProjectileEvents};totalProjectileHitEvents={totalProjectileHitEvents};hitsByTick=[{hitSummary}];projectileEventsByTick=[{projectileSummary}]";
    }
    private static string ComputePayloadHash(byte[] payload)
    {
        byte[] bytes = SHA256.HashData(payload);
        StringBuilder sb = new(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    private static ServerHost CreateHost(ServerConfig config)
    {
        TileMap map = CreateObstacleMap();
        ImmutableArray<EntityState> entities =
        [
            new EntityState(new EntityId(ObserverEntityId), At(2, 2), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 8),
            new EntityState(new EntityId(TargetEntityId), At(8, 2), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 8)
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
                new BootstrapPlayerRecord("pr89-observer", 8901001, ObserverEntityId, ZoneIdValue),
                new BootstrapPlayerRecord("pr89-target", 8901002, TargetEntityId, ZoneIdValue)));

        return new ServerHost(config, bootstrap: bootstrap);
    }

    private static TileMap CreateObstacleMap()
    {
        const int width = 12;
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
            if (y == 4)
            {
                continue;
            }

            tiles[(y * width) + 5] = TileKind.Solid;
        }

        return new TileMap(width, height, tiles.ToImmutableArray());
    }

    private static Vec2Fix At(int x, int y) => new(Fix32.FromInt(x), Fix32.FromInt(y));

    private static void HandshakeAndEnter(InMemoryEndpoint endpoint, string accountId)
    {
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, accountId)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(ZoneIdValue)));
    }

    private readonly record struct StepCommand(sbyte ObserverMoveX, sbyte ObserverMoveY, sbyte TargetMoveX, sbyte TargetMoveY, bool CastPoint);
}

public sealed record CombatScenarioRun(
    List<SnapshotV2> ObserverSnapshots,
    Dictionary<int, IReadOnlySet<int>> ExpectedVisibleByTick,
    int ObserverSessionId,
    int ObserverEntityId,
    int TargetEntityId,
    int CastTick,
    List<int> SpawnTicks,
    List<int> DespawnTicks,
    List<string> ObserverPayloadHashes,
    string FinalChecksum,
    string DiagnosticsSummary);
