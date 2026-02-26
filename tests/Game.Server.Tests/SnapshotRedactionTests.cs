using System.Collections.Immutable;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "PR82")]
public sealed class SnapshotRedactionTests
{
    [Fact]
    [Trait("Category", "Canary")]
    public void SnapshotV2_RedactsInvisibleEntities_FromEntitiesAndDeltaPayloads()
    {
        ServerHost host = SnapshotRedactionTestHelpers.CreateHostForTwoFactionWorld();
        InMemoryEndpoint endpointA = new();
        InMemoryEndpoint endpointB = new();

        host.Connect(endpointA);
        host.Connect(endpointB);
        SnapshotRedactionTestHelpers.HandshakeAndEnter(endpointA, "acc-a");
        SnapshotRedactionTestHelpers.HandshakeAndEnter(endpointB, "acc-b");

        host.AdvanceTicks(2);
        _ = SnapshotRedactionTestHelpers.DrainMessages(endpointA);
        _ = SnapshotRedactionTestHelpers.DrainMessages(endpointB);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));

        host.StepOnce();

        SnapshotV2 snapshotA = SnapshotRedactionTestHelpers.DrainMessages(endpointA).OfType<SnapshotV2>().Last();

        int[] entityIds = snapshotA.Entities.Select(entity => entity.EntityId).OrderBy(id => id).ToArray();
        int[] entersIds = snapshotA.Enters.Select(entity => entity.EntityId).OrderBy(id => id).ToArray();
        int[] updatesIds = snapshotA.Updates.Select(entity => entity.EntityId).OrderBy(id => id).ToArray();

        Assert.Equal(new[] { 11, 12 }, entityIds);
        Assert.All(entersIds, entityId => Assert.Contains(entityId, new[] { 11, 12 }));
        Assert.All(updatesIds, entityId => Assert.Contains(entityId, new[] { 11, 12 }));
        Assert.DoesNotContain(snapshotA.Entities, entity => entity.EntityId is 21 or 22);
        Assert.DoesNotContain(snapshotA.Enters, entity => entity.EntityId is 21 or 22);
        Assert.DoesNotContain(snapshotA.Updates, entity => entity.EntityId is 21 or 22);
        Assert.DoesNotContain(snapshotA.Leaves, entityId => entityId is 21 or 22);
    }
}

[Trait("Category", "PR82")]
public sealed class EntityPayloadIsolationTests
{
    [Fact]
    [Trait("Category", "PR82")]
    [Trait("Category", "Canary")]
    public void SnapshotV2_Leaves_KeepId_WhenEntityLeavesZone()
    {
        ServerHost host = SnapshotRedactionTestHelpers.CreateHostForVisibilityTransitionWorld();
        InMemoryEndpoint endpointA = new();
        InMemoryEndpoint endpointB = new();

        host.Connect(endpointA);
        host.Connect(endpointB);
        SnapshotRedactionTestHelpers.HandshakeAndEnter(endpointA, "acc-a");
        SnapshotRedactionTestHelpers.HandshakeAndEnter(endpointB, "acc-b");

        host.AdvanceTicks(2);
        _ = SnapshotRedactionTestHelpers.DrainMessages(endpointA);
        _ = SnapshotRedactionTestHelpers.DrainMessages(endpointB);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        host.StepOnce();
        _ = SnapshotRedactionTestHelpers.DrainMessages(endpointA);

        endpointB.EnqueueToServer(ProtocolCodec.Encode(new LeaveZoneRequestV2(1)));

        SnapshotV2 leaveSnapshot = SnapshotRedactionTestHelpers.WaitForSnapshotCondition(
            host,
            endpointA,
            snapshot => snapshot.Leaves.Contains(21),
            maxSteps: 64);

        Assert.Contains(21, leaveSnapshot.Leaves);
    }

    [Fact]
    [Trait("Category", "PR82")]
    [Trait("Category", "Canary")]
    public void SnapshotV2_RemovesEntityPayload_WhenEntityTransitionsVisibleToInvisible()
    {
        ServerHost host = SnapshotRedactionTestHelpers.CreateHostForVisibilityTransitionWorld();
        InMemoryEndpoint endpointA = new();
        InMemoryEndpoint endpointB = new();

        host.Connect(endpointA);
        host.Connect(endpointB);
        SnapshotRedactionTestHelpers.HandshakeAndEnter(endpointA, "acc-a");
        SnapshotRedactionTestHelpers.HandshakeAndEnter(endpointB, "acc-b");

        host.AdvanceTicks(2);
        _ = SnapshotRedactionTestHelpers.DrainMessages(endpointA);
        _ = SnapshotRedactionTestHelpers.DrainMessages(endpointB);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        host.StepOnce();

        SnapshotV2 visibleSnapshot = SnapshotRedactionTestHelpers.DrainMessages(endpointA).OfType<SnapshotV2>().Last();
        Assert.Contains(visibleSnapshot.Entities, entity => entity.EntityId == 21);

        int nextTick = visibleSnapshot.Tick + 1;
        SnapshotV2? invisibleSnapshot = null;

        for (int step = 0; step < 20; step++)
        {
            endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(nextTick++, 1, 0)));
            host.StepOnce();

            SnapshotV2 latest = SnapshotRedactionTestHelpers.DrainMessages(endpointA).OfType<SnapshotV2>().Last();
            if (latest.Entities.All(entity => entity.EntityId != 21))
            {
                invisibleSnapshot = latest;
                break;
            }
        }

        Assert.NotNull(invisibleSnapshot);
        Assert.DoesNotContain(invisibleSnapshot!.Entities, entity => entity.EntityId == 21);
        Assert.DoesNotContain(invisibleSnapshot.Enters, entity => entity.EntityId == 21);
        Assert.DoesNotContain(invisibleSnapshot.Updates, entity => entity.EntityId == 21);
        Assert.DoesNotContain(invisibleSnapshot.Leaves, entityId => entityId == 21);
    }
}

file static class SnapshotRedactionTestHelpers
{
    public static ServerHost CreateHostForTwoFactionWorld()
    {
        TileMap map = OpenMap(12, 12);
        ImmutableArray<EntityState> entities =
        [
            new EntityState(new EntityId(11), At(1, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 1),
            new EntityState(new EntityId(12), At(2, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 1),
            new EntityState(new EntityId(21), At(9, 9), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 1),
            new EntityState(new EntityId(22), At(10, 9), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 1)
        ];

        return CreateHost(map, entities);
    }

    public static ServerHost CreateHostForVisibilityTransitionWorld()
    {
        TileMap map = OpenMap(12, 12);
        ImmutableArray<EntityState> entities =
        [
            new EntityState(new EntityId(11), At(1, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 3),
            new EntityState(new EntityId(21), At(3, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 0)
        ];

        return CreateHost(map, entities);
    }

    public static void HandshakeAndEnter(InMemoryEndpoint endpoint, string accountId)
    {
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, accountId)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
    }

    public static SnapshotV2 WaitForSnapshotCondition(
        ServerHost host,
        InMemoryEndpoint observerEndpoint,
        Func<SnapshotV2, bool> predicate,
        int maxSteps)
    {
        for (int i = 0; i < maxSteps; i++)
        {
            host.StepOnce();

            while (observerEndpoint.TryDequeueFromServer(out byte[] payload))
            {
                if (!ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _) || message is not SnapshotV2 snapshot)
                {
                    continue;
                }

                observerEndpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq)));
                if (predicate(snapshot))
                {
                    return snapshot;
                }
            }
        }

        Assert.Fail($"Condition not met within {maxSteps} steps.");
        return null!;
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

    private static ServerHost CreateHost(TileMap map, ImmutableArray<EntityState> entities)
    {
        ZoneState zone = new(new ZoneId(1), map, entities);
        ImmutableArray<EntityLocation> locations = entities
            .OrderBy(entity => entity.Id.Value)
            .Select(entity => new EntityLocation(entity.Id, zone.Id))
            .ToImmutableArray();

        WorldState world = new(0, ImmutableArray.Create(zone), locations);

        ServerBootstrap bootstrap = new(
            world,
            ServerConfig.Default(seed: 1082).Seed,
            ImmutableArray.Create(
                new BootstrapPlayerRecord("acc-a", 1001, 11, 1),
                new BootstrapPlayerRecord("acc-b", 1002, 21, 1)));

        ServerConfig config = ServerConfig.Default(seed: 1082) with { SnapshotEveryTicks = 1 };
        return new ServerHost(config, bootstrap: bootstrap);
    }

    private static TileMap OpenMap(int width, int height)
        => new(width, height, Enumerable.Repeat(TileKind.Empty, checked(width * height)).ToImmutableArray());

    private static Vec2Fix At(int x, int y)
        => new(Fix32.FromInt(x), Fix32.FromInt(y));
}
