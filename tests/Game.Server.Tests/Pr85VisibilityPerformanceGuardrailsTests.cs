using System.Collections.Immutable;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "PR85")]
public sealed class VisibilityCostBudgetTests
{
    [Fact]
    [Trait("Category", "Canary")]
    public void VisibilityPipeline_CostCountersStayWithinDeterministicBudget()
    {
        (ServerHost host, InMemoryEndpoint endpointA, InMemoryEndpoint endpointB) = Pr85PerfHelpers.CreateGuardrailHost();
        Pr85PerfHelpers.HandshakeAndEnter(endpointA, "pr85-a");
        Pr85PerfHelpers.HandshakeAndEnter(endpointB, "pr85-b");

        host.AdvanceTicks(2);
        _ = Pr85PerfHelpers.DrainMessages(endpointA);
        _ = Pr85PerfHelpers.DrainMessages(endpointB);
        _ = host.SnapshotAndResetPerfWindow();

        int tick = host.CurrentWorld.Tick + 1;
        for (int i = 0; i < Pr85PerfBudgets.TickCount; i++)
        {
            endpointA.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, i + 1)));
            endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, i + 1)));

            int axis = (i % 2 == 0) ? 1 : -1;
            endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(tick++, axis, 0)));

            host.StepOnce();
            _ = Pr85PerfHelpers.DrainMessages(endpointA);
            _ = Pr85PerfHelpers.DrainMessages(endpointB);
        }

        PerfSnapshot snapshot = host.SnapshotAndResetPerfWindow();
        PerfBudgetConfig budget = Pr85PerfBudgets.BuildBudget();
        BudgetResult result = PerfBudgetEvaluator.Evaluate(snapshot, budget);

        Assert.True(result.Ok, string.Join(" | ", result.Violations));
        Assert.Equal(Pr85PerfBudgets.TickCount, snapshot.TickCount);
        Assert.InRange(snapshot.MaxVisibilityCellsVisitedPerTick, 1, Pr85PerfBudgets.MaxVisibilityCellsVisitedPerTick);
        Assert.InRange(snapshot.MaxVisibilityRaysCastPerTick, 1, Pr85PerfBudgets.MaxVisibilityRaysCastPerTick);
    }
}

[Trait("Category", "PR85")]
public sealed class RedactionAndAoiBudgetTests
{
    [Fact]
    [Trait("Category", "Canary")]
    public void RedactionAndAoi_CountersAndTransitionsStayBoundedAndCanonical()
    {
        (ServerHost host, InMemoryEndpoint endpointA, InMemoryEndpoint endpointB) = Pr85PerfHelpers.CreateGuardrailHost();
        Pr85PerfHelpers.HandshakeAndEnter(endpointA, "pr85-a");
        Pr85PerfHelpers.HandshakeAndEnter(endpointB, "pr85-b");

        host.AdvanceTicks(2);
        _ = Pr85PerfHelpers.DrainMessages(endpointA);
        _ = Pr85PerfHelpers.DrainMessages(endpointB);
        _ = host.SnapshotAndResetPerfWindow();

        int tick = host.CurrentWorld.Tick + 1;
        for (int i = 0; i < Pr85PerfBudgets.TickCount; i++)
        {
            endpointA.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, i + 1)));
            endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, i + 1)));

            int moveX = (i % 3) - 1;
            endpointA.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(tick++, moveX, 0)));
            host.StepOnce();

            foreach (SnapshotV2 snapshot in Pr85PerfHelpers.DrainMessages(endpointA).OfType<SnapshotV2>())
            {
                Assert.Equal(snapshot.Enters.Select(e => e.EntityId).Distinct().Count(), snapshot.Enters.Length);
                Assert.Equal(snapshot.Leaves.Distinct().Count(), snapshot.Leaves.Length);
            }

            _ = Pr85PerfHelpers.DrainMessages(endpointB);
        }

        PerfSnapshot snapshotPerf = host.SnapshotAndResetPerfWindow();
        Assert.True(snapshotPerf.TotalRedactionEntitiesEmitted <= snapshotPerf.TotalAoiEntitiesConsidered * 3);
        Assert.True(snapshotPerf.TotalRedactionEntitiesEmitted <= Pr85PerfBudgets.MaxEntitiesEmittedPerSession);
        Assert.True(snapshotPerf.MaxTransitionSpawnsPerTick <= Pr85PerfBudgets.MaxTransitionSpawnsPerTick);
        Assert.True(snapshotPerf.MaxTransitionDespawnsPerTick <= Pr85PerfBudgets.MaxTransitionDespawnsPerTick);
    }
}

file static class Pr85PerfHelpers
{
    public static (ServerHost Host, InMemoryEndpoint EndpointA, InMemoryEndpoint EndpointB) CreateGuardrailHost()
    {
        const int width = 16;
        const int height = 16;
        TileKind[] tiles = Enumerable.Repeat(TileKind.Empty, width * height).ToArray();

        for (int y = 2; y < 14; y++)
        {
            if (y == 8)
            {
                continue;
            }

            tiles[(y * width) + 8] = TileKind.Solid;
        }

        TileMap map = new(width, height, tiles.ToImmutableArray());
        ImmutableArray<EntityState> entities =
        [
            new(new EntityId(11), At(2, 2), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 4),
            new(new EntityId(12), At(3, 2), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 4),
            new(new EntityId(13), At(4, 2), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 4),
            new(new EntityId(14), At(5, 2), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 4),
            new(new EntityId(21), At(12, 12), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 4),
            new(new EntityId(22), At(11, 12), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 4),
            new(new EntityId(23), At(10, 12), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 4),
            new(new EntityId(24), At(9, 12), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 4)
        ];

        ZoneState zone = new(new ZoneId(1), map, entities);
        ImmutableArray<EntityLocation> locations = entities
            .OrderBy(entity => entity.Id.Value)
            .Select(entity => new EntityLocation(entity.Id, zone.Id))
            .ToImmutableArray();

        WorldState world = new(0, ImmutableArray.Create(zone), locations);
        ServerConfig cfg = ServerConfig.Default(seed: 8501) with { SnapshotEveryTicks = 1, DisconnectGraceTicks = 5 };
        ServerBootstrap bootstrap = new(
            world,
            cfg.Seed,
            ImmutableArray.Create(
                new BootstrapPlayerRecord("pr85-a", 4001, 11, 1),
                new BootstrapPlayerRecord("pr85-b", 4002, 21, 1)));

        ServerHost host = new(cfg, bootstrap: bootstrap);
        InMemoryEndpoint endpointA = new();
        InMemoryEndpoint endpointB = new();
        host.Connect(endpointA);
        host.Connect(endpointB);
        return (host, endpointA, endpointB);
    }

    public static void HandshakeAndEnter(InMemoryEndpoint endpoint, string accountId)
    {
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, accountId)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
    }

    public static List<IServerMessage> DrainMessages(InMemoryEndpoint endpoint)
    {
        List<IServerMessage> decoded = new();
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _) && message is not null)
            {
                decoded.Add(message);
            }
        }

        return decoded;
    }

    private static Vec2Fix At(int x, int y)
        => new(Fix32.FromInt(x), Fix32.FromInt(y));
}
