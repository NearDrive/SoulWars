using Game.Audit;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class AuditTrailTests
{
    [Fact]
    public void AuditLog_IsOrdered_ByTickThenSeq()
    {
        InMemoryAuditSink audit = new();
        ServerHost host = new(ServerConfig.Default(seed: 321) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 2,
            NpcCountPerZone = 0,
            DisconnectGraceTicks = 2000
        }, auditSink: audit);

        (InMemoryEndpoint endpointA, _, EnterZoneAck ackA) = ConnectAndEnterWithAck(host, "alice", 1);
        (InMemoryEndpoint endpointB, _, EnterZoneAck ackB) = ConnectAndEnterWithAck(host, "bob", 1);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new TeleportRequest(2)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new TeleportRequest(2)));
        host.ProcessInboundOnce();
        host.AdvanceSimulationOnce();

        bool sawDeath = false;
        for (int tick = 0; tick < 220 && !sawDeath; tick++)
        {
            endpointA.EnqueueToServer(ProtocolCodec.Encode(new AttackIntent(tick + 1, ackA.EntityId, ackB.EntityId, 2)));
            endpointB.EnqueueToServer(ProtocolCodec.Encode(new AttackIntent(tick + 1, ackB.EntityId, ackA.EntityId, 2)));
            host.ProcessInboundOnce();
            host.AdvanceSimulationOnce();

            sawDeath = audit.Events.Any(e => e.Header.Type == AuditEventType.Death);
        }

        Assert.True(sawDeath);
        Assert.NotEmpty(audit.Events);

        for (int i = 1; i < audit.Events.Count; i++)
        {
            AuditEvent prev = audit.Events[i - 1];
            AuditEvent cur = audit.Events[i];

            Assert.True(cur.Header.Tick > prev.Header.Tick ||
                        (cur.Header.Tick == prev.Header.Tick && cur.Header.Seq > prev.Header.Seq));
        }

        Assert.Contains(audit.Events, e => e.Header.Type == AuditEventType.PlayerConnected);
        Assert.Contains(audit.Events, e => e.Header.Type == AuditEventType.EnterZone);
        Assert.Contains(audit.Events, e => e.Header.Type == AuditEventType.Teleport);
        Assert.Contains(audit.Events, e => e.Header.Type == AuditEventType.Death);
    }

    [Fact]
    public void AuditReplay_Matches_LiveRun_WorldChecksum()
    {
        InMemoryAuditSink audit = new();
        ServerConfig config = ServerConfig.Default(seed: 555) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 1,
            NpcCountPerZone = 4
        };

        ServerHost live = new(config, auditSink: audit);

        ConnectAndEnter(live, "bot-a", 1);
        ConnectAndEnter(live, "bot-b", 1);

        live.AdvanceTicks(400);
        string liveChecksum = StateChecksum.Compute(live.CurrentWorld);

        byte[] bytes = audit.ToBytes();
        using MemoryStream stream = new(bytes);
        AuditLogReader reader = new(stream);
        IReadOnlyList<AuditEvent> events = reader.ReadAll().ToArray();

        WorldState replayed = AuditReplayer.Replay(config.ToSimulationConfig(), events, live.CurrentWorld.Tick);
        string replayChecksum = StateChecksum.Compute(replayed);

        Assert.Equal(liveChecksum, replayChecksum);
    }

    [Fact]
    public void Audit_Includes_DespawnDisconnected()
    {
        InMemoryAuditSink audit = new();
        ServerHost host = new(ServerConfig.Default(seed: 71) with
        {
            SnapshotEveryTicks = 1,
            DisconnectGraceTicks = 5
        }, auditSink: audit);

        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "alice")));
        host.ProcessInboundOnce();
        ReadSingle<Welcome>(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.ProcessInboundOnce();
        ReadSingle<EnterZoneAck>(endpoint);
        host.AdvanceSimulationOnce();

        endpoint.Close();
        host.ProcessInboundOnce();
        host.AdvanceTicks(6);

        Assert.Contains(audit.Events, e => e.Header.Type == AuditEventType.DespawnDisconnected);
    }

    private static void ConnectAndEnter(ServerHost host, string accountId, int zoneId)
    {
        _ = ConnectAndEnterWithAck(host, accountId, zoneId);
    }

    private static (InMemoryEndpoint Endpoint, Welcome Welcome, EnterZoneAck Ack) ConnectAndEnterWithAck(ServerHost host, string accountId, int zoneId)
    {
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", accountId)));
        host.ProcessInboundOnce();
        Welcome welcome = ReadSingle<Welcome>(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(zoneId)));
        host.ProcessInboundOnce();
        EnterZoneAck ack = ReadSingle<EnterZoneAck>(endpoint);
        host.AdvanceSimulationOnce();
        return (endpoint, welcome, ack);
    }

    private static T ReadSingle<T>(InMemoryEndpoint endpoint)
        where T : class, IServerMessage
    {
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            IServerMessage message = ProtocolCodec.DecodeServer(payload);
            if (message is T typed)
            {
                return typed;
            }
        }

        throw new Xunit.Sdk.XunitException($"Message {typeof(T).Name} not found.");
    }
}
