using System.Collections.Immutable;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "PR83")]
public sealed class VisibilityTransitionTests
{
    [Fact]
    [Trait("Category", "Canary")]
    public void SnapshotV2_EmitsSingleDeterministicSpawnAndDespawnTransitions()
    {
        ServerHost host = VisibilityTransitionPr83Helpers.CreateHostForTransitionWorld();
        InMemoryEndpoint endpointA = new();
        InMemoryEndpoint endpointB = new();

        host.Connect(endpointA);
        host.Connect(endpointB);
        VisibilityTransitionPr83Helpers.HandshakeAndEnter(endpointA, "acc-a");
        VisibilityTransitionPr83Helpers.HandshakeAndEnter(endpointB, "acc-b");

        host.AdvanceTicks(2);
        _ = VisibilityTransitionPr83Helpers.DrainMessages(endpointA);
        _ = VisibilityTransitionPr83Helpers.DrainMessages(endpointB);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        host.StepOnce();

        SnapshotV2 tickN = VisibilityTransitionPr83Helpers.DrainAndAckLatestSnapshot(endpointA);
        Assert.DoesNotContain(tickN.Entities, entity => entity.EntityId == 21);
        Assert.DoesNotContain(tickN.Enters, entity => entity.EntityId == 21);
        Assert.DoesNotContain(tickN.Leaves, entityId => entityId == 21);
        VisibilityTransitionPr83Helpers.AssertOrdered(tickN);

        int nextTick = tickN.Tick + 1;
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(nextTick++, -1, 0)));
        host.StepOnce();

        SnapshotV2 tickN1 = VisibilityTransitionPr83Helpers.DrainAndAckLatestSnapshot(endpointA);
        Assert.Equal(1, tickN1.Enters.Count(entity => entity.EntityId == 21));
        Assert.Contains(tickN1.Entities, entity => entity.EntityId == 21);
        Assert.DoesNotContain(tickN1.Leaves, entityId => entityId == 21);
        VisibilityTransitionPr83Helpers.AssertOrdered(tickN1);

        endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(nextTick++, 0, 0)));
        host.StepOnce();

        SnapshotV2 tickN2 = VisibilityTransitionPr83Helpers.DrainAndAckLatestSnapshot(endpointA);
        Assert.Contains(tickN2.Entities, entity => entity.EntityId == 21);
        Assert.DoesNotContain(tickN2.Enters, entity => entity.EntityId == 21);
        Assert.DoesNotContain(tickN2.Leaves, entityId => entityId == 21);
        VisibilityTransitionPr83Helpers.AssertOrdered(tickN2);

        endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(nextTick, 1, 0)));
        host.StepOnce();

        SnapshotV2 tickN3 = VisibilityTransitionPr83Helpers.DrainAndAckLatestSnapshot(endpointA);
        Assert.DoesNotContain(tickN3.Entities, entity => entity.EntityId == 21);
        Assert.Equal(1, tickN3.Leaves.Count(entityId => entityId == 21));
        Assert.DoesNotContain(tickN3.Enters, entity => entity.EntityId == 21);
        VisibilityTransitionPr83Helpers.AssertOrdered(tickN3);
    }
}

[Trait("Category", "PR83")]
public sealed class ReplayVerifyVisibilityTransitionScenario
{
    [Fact]
    [Trait("Category", "ReplayVerify")]
    public void ReplayVerify_VisibilityTransitionScenario_IsDeterministic()
    {
        (string checksumA, List<string> transitionsA) = RunVisibilityTransitionScenario();
        (string checksumB, List<string> transitionsB) = RunVisibilityTransitionScenario();

        Assert.Equal(TestChecksum.NormalizeFullHex(checksumA), TestChecksum.NormalizeFullHex(checksumB));
        Assert.Equal(transitionsA, transitionsB);
        Assert.Contains("spawn:21", transitionsA);
        Assert.Contains("despawn:21", transitionsA);
    }

    private static (string Checksum, List<string> Transitions) RunVisibilityTransitionScenario()
    {
        ServerHost host = VisibilityTransitionPr83Helpers.CreateHostForTransitionWorld();
        InMemoryEndpoint endpointA = new();
        InMemoryEndpoint endpointB = new();

        host.Connect(endpointA);
        host.Connect(endpointB);
        VisibilityTransitionPr83Helpers.HandshakeAndEnter(endpointA, "acc-a");
        VisibilityTransitionPr83Helpers.HandshakeAndEnter(endpointB, "acc-b");

        host.AdvanceTicks(2);
        _ = VisibilityTransitionPr83Helpers.DrainMessages(endpointA);
        _ = VisibilityTransitionPr83Helpers.DrainMessages(endpointB);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        host.StepOnce();

        SnapshotV2 baseline = VisibilityTransitionPr83Helpers.DrainAndAckLatestSnapshot(endpointA);
        int tick = baseline.Tick + 1;

        List<string> transitions = new();

        endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(tick++, -1, 0)));
        host.StepOnce();
        SnapshotV2 reveal = VisibilityTransitionPr83Helpers.DrainAndAckLatestSnapshot(endpointA);
        transitions.AddRange(reveal.Enters.Select(entity => $"spawn:{entity.EntityId}"));

        endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(tick++, 0, 0)));
        host.StepOnce();
        SnapshotV2 stable = VisibilityTransitionPr83Helpers.DrainAndAckLatestSnapshot(endpointA);
        transitions.AddRange(stable.Enters.Select(entity => $"spawn:{entity.EntityId}"));

        endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(tick, 1, 0)));
        host.StepOnce();
        SnapshotV2 hide = VisibilityTransitionPr83Helpers.DrainAndAckLatestSnapshot(endpointA);
        transitions.AddRange(hide.Leaves.Select(entityId => $"despawn:{entityId}"));

        return (StateChecksum.Compute(host.CurrentWorld), transitions);
    }
}

file static class VisibilityTransitionPr83Helpers
{
    public static ServerHost CreateHostForTransitionWorld()
    {
        TileMap map = OpenMap(12, 12);
        ImmutableArray<EntityState> entities =
        [
            new EntityState(new EntityId(11), At(1, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(1), VisionRadiusTiles: 2),
            new EntityState(new EntityId(21), At(4, 1), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0, FactionId: new FactionId(2), VisionRadiusTiles: 0)
        ];

        ZoneState zone = new(new ZoneId(1), map, entities);
        ImmutableArray<EntityLocation> locations = entities
            .OrderBy(entity => entity.Id.Value)
            .Select(entity => new EntityLocation(entity.Id, zone.Id))
            .ToImmutableArray();

        WorldState world = new(0, ImmutableArray.Create(zone), locations);

        ServerBootstrap bootstrap = new(
            world,
            ServerConfig.Default(seed: 1083).Seed,
            ImmutableArray.Create(
                new BootstrapPlayerRecord("acc-a", 1001, 11, 1),
                new BootstrapPlayerRecord("acc-b", 1002, 21, 1)));

        ServerConfig config = ServerConfig.Default(seed: 1083) with { SnapshotEveryTicks = 1 };
        return new ServerHost(config, bootstrap: bootstrap);
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

    public static SnapshotV2 DrainAndAckLatestSnapshot(InMemoryEndpoint endpoint)
    {
        SnapshotV2? latest = null;
        foreach (IServerMessage message in DrainMessages(endpoint))
        {
            if (message is SnapshotV2 snapshot)
            {
                latest = snapshot;
                endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq)));
            }
        }

        Assert.NotNull(latest);
        return latest!;
    }

    public static void AssertOrdered(SnapshotV2 snapshot)
    {
        Assert.Equal(snapshot.Entities.OrderBy(entity => entity.EntityId).Select(entity => entity.EntityId), snapshot.Entities.Select(entity => entity.EntityId));
        Assert.Equal(snapshot.Enters.OrderBy(entity => entity.EntityId).Select(entity => entity.EntityId), snapshot.Enters.Select(entity => entity.EntityId));
        Assert.Equal(snapshot.Updates.OrderBy(entity => entity.EntityId).Select(entity => entity.EntityId), snapshot.Updates.Select(entity => entity.EntityId));
        Assert.Equal(snapshot.Leaves.OrderBy(entityId => entityId), snapshot.Leaves);
    }

    private static TileMap OpenMap(int width, int height)
        => new(width, height, Enumerable.Repeat(TileKind.Empty, checked(width * height)).ToImmutableArray());

    private static Vec2Fix At(int x, int y)
        => new(Fix32.FromInt(x), Fix32.FromInt(y));
}
