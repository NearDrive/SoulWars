using System.Buffers.Binary;
using System.Security.Cryptography;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class ServerHostIntegrationTests
{
    [Fact]
    public void Connect_EnterZone_ReceivesInitialSnapshot()
    {
        ServerHost host = new(ServerConfig.Default(seed: 77) with { SnapshotEveryTicks = 1 });
        InMemoryEndpoint endpoint = new();

        SessionId sessionId = host.Connect(endpoint);
        Assert.Equal(1, sessionId.Value);

        Welcome welcome = AssertMessage<Welcome>(endpoint);
        Assert.Equal(sessionId, welcome.SessionId);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new Hello("test-client")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));

        host.AdvanceTicks(1);

        EnterZoneAck ack = AssertMessage<EnterZoneAck>(endpoint);
        Assert.Equal(1, ack.ZoneId);

        Snapshot snapshot = AssertMessage<Snapshot>(endpoint);
        Assert.Equal(1, snapshot.ZoneId);
        Assert.True(snapshot.Tick >= 1);
        Assert.Contains(snapshot.Entities, entity => entity.EntityId == ack.EntityId);
    }

    [Fact]
    public void MoveIntent_ChangesPositionInSnapshots()
    {
        ServerHost host = new(ServerConfig.Default(seed: 11) with { SnapshotEveryTicks = 1 });
        InMemoryEndpoint endpoint = new();

        host.Connect(endpoint);
        _ = AssertMessage<Welcome>(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.AdvanceTicks(1);

        EnterZoneAck ack = AssertMessage<EnterZoneAck>(endpoint);
        Snapshot initial = AssertMessage<Snapshot>(endpoint);
        SnapshotEntity initialEntity = Assert.Single(initial.Entities.Where(e => e.EntityId == ack.EntityId));

        int previousX = initialEntity.PosXRaw;
        bool moved = false;

        for (int step = 0; step < 15; step++)
        {
            endpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(initial.Tick + step + 1, 1, 0)));
            host.StepOnce();
            Snapshot snapshot = AssertMessage<Snapshot>(endpoint);
            SnapshotEntity entity = Assert.Single(snapshot.Entities.Where(e => e.EntityId == ack.EntityId));

            Assert.True(entity.PosXRaw >= previousX);
            if (entity.PosXRaw > previousX)
            {
                moved = true;
            }

            previousX = entity.PosXRaw;
        }

        Assert.True(moved, "Expected at least one positive X movement in snapshots.");
    }

    [Fact]
    public void Determinism_SameSeedSameInputs_SameSnapshotChecksums()
    {
        List<string> firstRun = RunScenarioAndCollectChecksums();
        List<string> secondRun = RunScenarioAndCollectChecksums();

        Assert.Equal(firstRun.Count, secondRun.Count);
        Assert.Equal(firstRun, secondRun);
    }

    private static List<string> RunScenarioAndCollectChecksums()
    {
        ServerHost host = new(ServerConfig.Default(seed: 123) with { SnapshotEveryTicks = 1 });
        InMemoryEndpoint endpoint = new();

        host.Connect(endpoint);
        _ = AssertMessage<Welcome>(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.AdvanceTicks(1);

        EnterZoneAck ack = AssertMessage<EnterZoneAck>(endpoint);
        _ = AssertMessage<Snapshot>(endpoint);

        List<string> checksums = new();
        Random deterministic = new(999);

        for (int tickOffset = 1; tickOffset <= 30; tickOffset++)
        {
            sbyte moveX = (sbyte)deterministic.Next(-1, 2);
            sbyte moveY = (sbyte)deterministic.Next(-1, 2);
            endpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(tickOffset + 1, moveX, moveY)));

            host.StepOnce();

            Snapshot snapshot = AssertMessage<Snapshot>(endpoint);
            checksums.Add(ComputeSnapshotChecksum(snapshot, ack.EntityId));
        }

        return checksums;
    }

    private static string ComputeSnapshotChecksum(Snapshot snapshot, int trackedEntityId)
    {
        SnapshotEntity[] ordered = snapshot.Entities
            .OrderBy(entity => entity.EntityId)
            .ToArray();

        byte[] data = new byte[(3 + (ordered.Length * 5)) * 4];
        int offset = 0;

        WriteInt(data, ref offset, snapshot.Tick);
        WriteInt(data, ref offset, snapshot.ZoneId);
        WriteInt(data, ref offset, trackedEntityId);

        foreach (SnapshotEntity entity in ordered)
        {
            WriteInt(data, ref offset, entity.EntityId);
            WriteInt(data, ref offset, entity.PosXRaw);
            WriteInt(data, ref offset, entity.PosYRaw);
            WriteInt(data, ref offset, entity.VelXRaw);
            WriteInt(data, ref offset, entity.VelYRaw);
        }

        return Convert.ToHexString(SHA256.HashData(data));
    }

    private static void WriteInt(byte[] data, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
        offset += 4;
    }

    private static T AssertMessage<T>(InMemoryEndpoint endpoint)
        where T : class
    {
        while (endpoint.TryDequeueToClient(out byte[] payload))
        {
            IServerMessage decoded = ProtocolCodec.DecodeServer(payload);
            if (decoded is T typed)
            {
                return typed;
            }
        }

        throw new Xunit.Sdk.XunitException($"No message of type {typeof(T).Name} was available.");
    }
}
