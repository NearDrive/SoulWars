using System.Collections.Immutable;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;
using Xunit.Abstractions;

namespace Game.Server.Tests;

[Trait("Category", "PR86")]
public sealed class VisibilityNoLeakInvariantTests
{
    [Fact]
    [Trait("Category", "PR86")]
    [Trait("Category", "Canary")]
    public void NoLeakInvariant_PassesOnScenario_AndFailsOnInjectedInvisibleId()
    {
        VisibilityScenarioRun run = VisibilityInvariantPr86Harness.RunVisibilityScenario();

        ServerInvariants.ValidateVisibilityStreamInvariants(
            run.ObserverSnapshots,
            tick => run.ExpectedVisibleByTick[tick],
            run.ObserverSessionId);

        SnapshotV2 hiddenTickSnapshot = run.ObserverSnapshots.First(snapshot => !run.ExpectedVisibleByTick[snapshot.Tick].Contains(run.TargetEntityId));
        SnapshotV2 injectedLeak = hiddenTickSnapshot with
        {
            Updates = hiddenTickSnapshot.Updates.Concat(new[]
            {
                new SnapshotEntity(run.TargetEntityId, 0, 0, Kind: SnapshotEntityKind.Player)
            }).ToArray()
        };

        List<SnapshotV2> tampered = run.ObserverSnapshots
            .Select(snapshot => snapshot.Tick == hiddenTickSnapshot.Tick ? injectedLeak : snapshot)
            .ToList();

        InvariantViolationException leak = Assert.Throws<InvariantViolationException>(() =>
            ServerInvariants.ValidateVisibilityStreamInvariants(
                tampered,
                tick => run.ExpectedVisibleByTick[tick],
                run.ObserverSessionId));

        Assert.Contains("NoLeakInvariant", leak.Message, StringComparison.Ordinal);
    }
}

[Trait("Category", "PR86")]
public sealed class SpawnDespawnSequenceInvariantTests
{
    private readonly ITestOutputHelper _output;

    public SpawnDespawnSequenceInvariantTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "PR86")]
    [Trait("Category", "Canary")]
    public void SpawnBeforeState_DespawnRemovesAll_RespawnRequired_AreEnforced()
    {
        VisibilityScenarioRun run = VisibilityInvariantPr86Harness.RunVisibilityScenario();

        ServerInvariants.ValidateVisibilityStreamInvariants(
            run.ObserverSnapshots,
            tick => run.ExpectedVisibleByTick[tick],
            run.ObserverSessionId);

        List<SnapshotV2> missingSpawnStream =
        [
            new SnapshotV2(
                Tick: 100,
                ZoneId: 1,
                SnapshotSeq: 1,
                IsFull: false,
                Entities: [new SnapshotEntity(run.TargetEntityId, 0, 0, Kind: SnapshotEntityKind.Player)],
                Leaves: [],
                Enters: [],
                Updates: []),
        ];

        _output.WriteLine($"spawn-check session={run.ObserverSessionId} target={run.TargetEntityId} ticks=[{string.Join(',', missingSpawnStream.Select(s => s.Tick))}] events=state-without-spawn");

        InvariantViolationException spawnViolation = Assert.Throws<InvariantViolationException>(() =>
            ServerInvariants.ValidateVisibilityStreamInvariants(
                missingSpawnStream,
                _ => new HashSet<int> { run.TargetEntityId },
                run.ObserverSessionId));
        Assert.Contains("SpawnBeforeStateInvariant", spawnViolation.Message, StringComparison.Ordinal);

        List<SnapshotV2> postDespawnWithoutRespawn =
        [
            new SnapshotV2(
                Tick: 200,
                ZoneId: 1,
                SnapshotSeq: 10,
                IsFull: false,
                Entities: [new SnapshotEntity(run.TargetEntityId, 0, 0, Kind: SnapshotEntityKind.Player)],
                Leaves: [],
                Enters: [new SnapshotEntity(run.TargetEntityId, 0, 0, Kind: SnapshotEntityKind.Player)],
                Updates: []),
            new SnapshotV2(
                Tick: 201,
                ZoneId: 1,
                SnapshotSeq: 11,
                IsFull: false,
                Entities: [],
                Leaves: [run.TargetEntityId],
                Enters: [],
                Updates: []),
            new SnapshotV2(
                Tick: 202,
                ZoneId: 1,
                SnapshotSeq: 12,
                IsFull: false,
                Entities: [],
                Leaves: [],
                Enters: [],
                Updates: [new SnapshotEntity(run.TargetEntityId, 1, 1, Kind: SnapshotEntityKind.Player)])
        ];

        Dictionary<int, IReadOnlySet<int>> visibleByTick = new()
        {
            [200] = new HashSet<int> { run.TargetEntityId },
            [201] = new HashSet<int>(),
            [202] = new HashSet<int> { run.TargetEntityId }
        };

        _output.WriteLine($"despawn-check session={run.ObserverSessionId} target={run.TargetEntityId} ticks=[{string.Join(',', postDespawnWithoutRespawn.Select(s => s.Tick))}] events=spawn,despawn,update-without-respawn validator=ServerInvariants.ValidateVisibilityStreamInvariants");

        InvariantViolationException despawnViolation = Assert.Throws<InvariantViolationException>(() =>
            ServerInvariants.ValidateVisibilityStreamInvariants(
                postDespawnWithoutRespawn,
                tick => visibleByTick[tick],
                run.ObserverSessionId));
        Assert.Contains("SpawnBeforeStateInvariant", despawnViolation.Message, StringComparison.Ordinal);
    }
}

[Trait("Category", "PR86")]
public sealed class CanonicalOrderingInvariantTests
{
    [Fact]
    [Trait("Category", "PR86")]
    [Trait("Category", "Canary")]
    public void CanonicalOrdering_IsEnforced()
    {
        VisibilityScenarioRun run = VisibilityInvariantPr86Harness.RunVisibilityScenario();

        ServerInvariants.ValidateVisibilityStreamInvariants(
            run.ObserverSnapshots,
            tick => run.ExpectedVisibleByTick[tick],
            run.ObserverSessionId);

        SnapshotV2 ordered = run.ObserverSnapshots.First(snapshot => snapshot.Entities.Length > 1);
        SnapshotV2 unsorted = ordered with
        {
            Entities = ordered.Entities.OrderByDescending(entity => entity.EntityId).ToArray()
        };

        List<SnapshotV2> tampered = run.ObserverSnapshots
            .Select(snapshot => snapshot.Tick == ordered.Tick ? unsorted : snapshot)
            .ToList();

        InvariantViolationException orderingViolation = Assert.Throws<InvariantViolationException>(() =>
            ServerInvariants.ValidateVisibilityStreamInvariants(
                tampered,
                tick => run.ExpectedVisibleByTick[tick],
                run.ObserverSessionId));

        Assert.Contains("invariant=CanonicalOrder", orderingViolation.Message, StringComparison.Ordinal);
    }
}


[Trait("Category", "PR86")]
public sealed class VisibilityRetransmitInvariantTests
{

    [Fact]
    [Trait("Category", "PR86")]
    [Trait("Category", "Canary")]
    public void FullSnapshot_AllowsImplicitInitialVisibilityWithoutEnters()
    {
        List<SnapshotV2> stream =
        [
            new SnapshotV2(
                Tick: 10,
                ZoneId: 1,
                SnapshotSeq: 1,
                IsFull: true,
                Entities: [new SnapshotEntity(100, 0, 0, Kind: SnapshotEntityKind.Player)],
                Leaves: [],
                Enters: [],
                Updates: []),
            new SnapshotV2(
                Tick: 11,
                ZoneId: 1,
                SnapshotSeq: 2,
                IsFull: false,
                Entities: [new SnapshotEntity(100, 1, 0, Kind: SnapshotEntityKind.Player)],
                Leaves: [],
                Enters: [],
                Updates: [new SnapshotEntity(100, 1, 0, Kind: SnapshotEntityKind.Player)])
        ];

        Dictionary<int, IReadOnlySet<int>> visibleByTick = new()
        {
            [10] = new HashSet<int> { 100 },
            [11] = new HashSet<int> { 100 }
        };

        Exception? ex = Record.Exception(() =>
            ServerInvariants.ValidateVisibilityStreamInvariants(
                stream,
                tick => visibleByTick[tick],
                sessionId: 8602));

        Assert.Null(ex);
    }

    [Fact]
    [Trait("Category", "PR86")]
    [Trait("Category", "Canary")]
    public void RetransmittedTicks_AreAccepted_WithoutBreakingLifecycleValidation()
    {
        List<SnapshotV2> stream =
        [
            new SnapshotV2(
                Tick: 10,
                ZoneId: 1,
                SnapshotSeq: 1,
                IsFull: true,
                Entities: [new SnapshotEntity(100, 0, 0, Kind: SnapshotEntityKind.Player)],
                Leaves: [],
                Enters: [new SnapshotEntity(100, 0, 0, Kind: SnapshotEntityKind.Player)],
                Updates: []),
            new SnapshotV2(
                Tick: 10,
                ZoneId: 1,
                SnapshotSeq: 1,
                IsFull: true,
                Entities: [new SnapshotEntity(100, 0, 0, Kind: SnapshotEntityKind.Player)],
                Leaves: [],
                Enters: [new SnapshotEntity(100, 0, 0, Kind: SnapshotEntityKind.Player)],
                Updates: []),
            new SnapshotV2(
                Tick: 11,
                ZoneId: 1,
                SnapshotSeq: 2,
                IsFull: false,
                Entities: [new SnapshotEntity(100, 1, 0, Kind: SnapshotEntityKind.Player)],
                Leaves: [],
                Enters: [],
                Updates: [new SnapshotEntity(100, 1, 0, Kind: SnapshotEntityKind.Player)])
        ];

        Dictionary<int, IReadOnlySet<int>> visibleByTick = new()
        {
            [10] = new HashSet<int> { 100 },
            [11] = new HashSet<int> { 100 }
        };

        Exception? ex = Record.Exception(() =>
            ServerInvariants.ValidateVisibilityStreamInvariants(
                stream,
                tick => visibleByTick[tick],
                sessionId: 8601));

        Assert.Null(ex);
    }
}

file static class VisibilityInvariantPr86Harness
{
    private const int ZoneIdValue = 1;
    private const int ObserverEntityId = 31;
    private const int TargetEntityId = 41;

    public static VisibilityScenarioRun RunVisibilityScenario()
    {
        ServerConfig config = ServerConfig.Default(8601) with
        {
            SnapshotEveryTicks = 1,
            VisionRadius = Fix32.FromInt(8),
            VisionRadiusSq = Fix32.FromInt(64),
            NpcCountPerZone = 0
        };

        ServerHost host = CreateHost(config);
        InMemoryEndpoint observerEndpoint = new();
        InMemoryEndpoint targetEndpoint = new();
        host.Connect(observerEndpoint);
        host.Connect(targetEndpoint);

        HandshakeAndEnter(observerEndpoint, "pr86-observer");
        HandshakeAndEnter(targetEndpoint, "pr86-target");
        observerEndpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ZoneIdValue, 0)));
        targetEndpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ZoneIdValue, 0)));

        int observerSessionId = -1;
        int observerRuntimeEntityId = -1;
        int targetRuntimeEntityId = -1;

        host.AdvanceTicks(2);
        _ = DrainAndAck(observerEndpoint, ref observerSessionId, ref observerRuntimeEntityId, out _);
        int ignoredSessionId = -1;
        _ = DrainAndAck(targetEndpoint, ref ignoredSessionId, ref targetRuntimeEntityId, out _);

        Assert.Equal(ObserverEntityId, observerRuntimeEntityId);
        Assert.Equal(TargetEntityId, targetRuntimeEntityId);

        VisibilityAoiProvider aoiProvider = new();
        Dictionary<int, IReadOnlySet<int>> expectedVisibleByTick = new();
        List<SnapshotV2> observerSnapshots = new();

        int inputTick = host.CurrentWorld.Tick + 1;
        bool hiddenObserved = false;
        bool shownAgainObserved = false;

        for (int i = 0; i < 300 && !shownAgainObserved; i++)
        {
            sbyte moveY = hiddenObserved ? (sbyte)1 : (sbyte)-1;
            targetEndpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(inputTick++, 0, moveY)));
            host.StepOnce();

            SnapshotV2? observerSnapshot = DrainAndAck(observerEndpoint, ref observerSessionId, ref observerRuntimeEntityId, out _);
            _ = DrainAndAck(targetEndpoint, ref ignoredSessionId, ref targetRuntimeEntityId, out _);

            if (observerSnapshot is null)
            {
                continue;
            }

            observerSnapshots.Add(observerSnapshot);
            ImmutableHashSet<int> visible = aoiProvider.ComputeVisible(host.CurrentWorld, new ZoneId(ZoneIdValue), new EntityId(observerRuntimeEntityId)).EntityIds
                .Select(entityId => entityId.Value)
                .ToImmutableHashSet();
            expectedVisibleByTick[observerSnapshot.Tick] = visible;

            if (observerSnapshot.Leaves.Contains(targetRuntimeEntityId))
            {
                hiddenObserved = true;
            }

            if (hiddenObserved && observerSnapshot.Enters.Any(entity => entity.EntityId == targetRuntimeEntityId))
            {
                shownAgainObserved = true;
            }
        }

        Assert.NotEmpty(observerSnapshots);
        Assert.True(hiddenObserved, "Expected a despawn transition (visible->invisible).");
        Assert.True(shownAgainObserved, "Expected a respawn transition (invisible->visible).");

        return new VisibilityScenarioRun(observerSnapshots, expectedVisibleByTick, observerSessionId, targetRuntimeEntityId);
    }

    private static SnapshotV2? DrainAndAck(InMemoryEndpoint endpoint, ref int sessionId, ref int entityId, out int snapshotCount)
    {
        SnapshotV2? latestSnapshot = null;
        snapshotCount = 0;

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
                snapshotCount++;
                endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq)));
            }
        }

        return latestSnapshot;
    }

    private static ServerHost CreateHost(ServerConfig config)
    {
        TileMap map = CreateObstacleMap();
        ImmutableArray<EntityState> entities =
        [
            new EntityState(new EntityId(ObserverEntityId), At(2, 3), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 8),
            new EntityState(new EntityId(TargetEntityId), At(6, 3), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 8)
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
                new BootstrapPlayerRecord("pr86-observer", 8601001, ObserverEntityId, ZoneIdValue),
                new BootstrapPlayerRecord("pr86-target", 8601002, TargetEntityId, ZoneIdValue)));

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

    private static Vec2Fix At(int x, int y) => new(Fix32.FromInt(x), Fix32.FromInt(y));

    private static void HandshakeAndEnter(InMemoryEndpoint endpoint, string accountId)
    {
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, accountId)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(ZoneIdValue)));
    }
}

public sealed record VisibilityScenarioRun(
    List<SnapshotV2> ObserverSnapshots,
    Dictionary<int, IReadOnlySet<int>> ExpectedVisibleByTick,
    int ObserverSessionId,
    int TargetEntityId);
