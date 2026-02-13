using Game.BotRunner;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class VisibilitySnapshotTests
{
    [Fact]
    public void Snapshots_ExcludeOutOfRangeEntities_Symmetrically()
    {
        Fix32 visionRadius = Fix32.FromInt(2);
        ServerHost host = new(ServerConfig.Default(seed: 500) with
        {
            SnapshotEveryTicks = 1,
            VisionRadius = visionRadius,
            VisionRadiusSq = visionRadius * visionRadius
        });

        InMemoryEndpoint endpointA = new();
        InMemoryEndpoint endpointB = new();
        host.Connect(endpointA);
        host.Connect(endpointB);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("A", "A")));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("B", "B")));
        endpointA.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));

        host.AdvanceTicks(2);

        (EnterZoneAck ackA, Snapshot snapshotA) = ReadEnterZoneAckAndLastSnapshot(endpointA);
        (EnterZoneAck ackB, Snapshot snapshotB) = ReadEnterZoneAckAndLastSnapshot(endpointB);

        SnapshotEntity selfA = snapshotA.Entities.Single(e => e.EntityId == ackA.EntityId);
        SnapshotEntity selfB = snapshotB.Entities.Single(e => e.EntityId == ackB.EntityId);

        sbyte moveX = selfB.PosXRaw >= selfA.PosXRaw ? (sbyte)1 : (sbyte)-1;
        sbyte moveY = selfB.PosYRaw >= selfA.PosYRaw ? (sbyte)1 : (sbyte)-1;
        if (selfB.PosXRaw == selfA.PosXRaw && selfB.PosYRaw == selfA.PosYRaw)
        {
            moveX = 1;
            moveY = 0;
        }

        bool bMissingFromA = false;
        bool aMissingFromB = false;

        for (int i = 0; i < 200; i++)
        {
            endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(snapshotB.Tick + 1, moveX, moveY)));
            host.StepOnce();

            snapshotA = ReadLastSnapshot(endpointA);
            snapshotB = ReadLastSnapshot(endpointB);

            bMissingFromA = snapshotA.Entities.All(e => e.EntityId != ackB.EntityId);
            aMissingFromB = snapshotB.Entities.All(e => e.EntityId != ackA.EntityId);
            if (bMissingFromA && aMissingFromB)
            {
                break;
            }
        }

        Assert.True(bMissingFromA);
        Assert.True(aMissingFromB);
    }

    [Fact]
    public void ScenarioRunner_WithVisibilityFiltering_IsDeterministic()
    {
        ScenarioConfig cfg = new(
            ServerSeed: 812,
            TickCount: 300,
            SnapshotEveryTicks: 1,
            BotCount: 3,
            ZoneId: 1,
            BaseBotSeed: 4100,
            NpcCount: 0,
            VisionRadiusTiles: 3);

        string checksum1 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));
        string checksum2 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));

        Assert.Equal(checksum1, checksum2);
    }

    [Fact]
    public void Snapshot_AlwaysContainsSelf_WhenVisionRadiusIsZero()
    {
        ServerHost host = new(ServerConfig.Default(seed: 401) with
        {
            SnapshotEveryTicks = 1,
            VisionRadius = Fix32.Zero,
            VisionRadiusSq = Fix32.Zero
        });

        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("solo", "solo")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));

        host.AdvanceTicks(2);

        (EnterZoneAck ack, Snapshot snapshot) = ReadEnterZoneAckAndLastSnapshot(endpoint);

        Assert.Contains(snapshot.Entities, entity => entity.EntityId == ack.EntityId);
    }

    private static (EnterZoneAck Ack, Snapshot Snapshot) ReadEnterZoneAckAndLastSnapshot(InMemoryEndpoint endpoint)
    {
        EnterZoneAck? ack = null;
        Snapshot? snapshot = null;

        while (endpoint.TryDequeueFromServer(out byte[] msg))
        {
            if (!ProtocolCodec.TryDecodeServer(msg, out IServerMessage? decoded, out _))
            {
                continue;
            }

            switch (decoded)
            {
                case EnterZoneAck typedAck:
                    ack = typedAck;
                    break;
                case Snapshot typedSnapshot:
                    snapshot = typedSnapshot;
                    break;
            }
        }

        Assert.NotNull(ack);
        Assert.NotNull(snapshot);
        return (ack!, snapshot!);
    }

    private static Snapshot ReadLastSnapshot(InMemoryEndpoint endpoint)
    {
        Snapshot? snapshot = null;
        while (endpoint.TryDequeueFromServer(out byte[] msg))
        {
            if (ProtocolCodec.TryDecodeServer(msg, out IServerMessage? decoded, out _) && decoded is Snapshot typed)
            {
                snapshot = typed;
            }
        }

        Assert.NotNull(snapshot);
        return snapshot!;
    }
}
