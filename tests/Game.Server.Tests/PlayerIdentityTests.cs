using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class PlayerIdentityTests
{
    [Fact]
    public void PlayerId_IsStable_ForSameAccountId()
    {
        ServerHost host = new(ServerConfig.Default(seed: 7));

        InMemoryEndpoint first = new();
        host.Connect(first);
        first.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "alice")));
        host.ProcessInboundOnce();
        Welcome welcomeFirst = ReadSingle<Welcome>(first);

        first.Close();
        host.ProcessInboundOnce();

        InMemoryEndpoint second = new();
        host.Connect(second);
        second.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "alice")));
        host.ProcessInboundOnce();
        Welcome welcomeSecond = ReadSingle<Welcome>(second);

        Assert.Equal(welcomeFirst.PlayerId, welcomeSecond.PlayerId);
    }

    [Fact]
    public void Reconnect_WithinGrace_ReusesEntity()
    {
        ServerHost host = new(ServerConfig.Default(seed: 8) with { SnapshotEveryTicks = 1, DisconnectGraceTicks = 200 });

        (InMemoryEndpoint first, Welcome welcome, EnterZoneAck firstAck) = ConnectAndEnter(host, "alice", 1);
        first.Close();
        host.ProcessInboundOnce();

        host.AdvanceTicks(50);

        (_, _, EnterZoneAck secondAck) = ConnectAndEnter(host, "alice", 1);

        Assert.Equal(firstAck.EntityId, secondAck.EntityId);
        Assert.True(host.TryGetPlayerState(welcome.PlayerId, out PlayerState state));
        Assert.True(state.IsConnected);
        Assert.Null(state.DespawnAtTick);
    }

    [Fact]
    public void Reconnect_AfterGrace_RespawnsNewEntity()
    {
        ServerHost host = new(ServerConfig.Default(seed: 9) with { SnapshotEveryTicks = 1, DisconnectGraceTicks = 50 });

        (InMemoryEndpoint first, _, EnterZoneAck firstAck) = ConnectAndEnter(host, "alice", 1);
        first.Close();
        host.ProcessInboundOnce();

        host.AdvanceTicks(60);

        (_, Welcome welcome, EnterZoneAck secondAck) = ConnectAndEnter(host, "alice", 1);

        Assert.NotEqual(firstAck.EntityId, secondAck.EntityId);
        Assert.False(host.WorldContainsEntity(firstAck.EntityId));
        Assert.True(host.TryGetPlayerState(welcome.PlayerId, out PlayerState state));
        Assert.Equal(secondAck.EntityId, state.EntityId);
    }

    [Fact]
    public void MultipleReconnects_NoDuplicateEntities()
    {
        ServerHost host = new(ServerConfig.Default(seed: 10) with { SnapshotEveryTicks = 1, DisconnectGraceTicks = 200 });

        PlayerId? playerId = null;
        for (int i = 0; i < 5; i++)
        {
            (InMemoryEndpoint endpoint, Welcome welcome, _) = ConnectAndEnter(host, "alice", 1);
            playerId = welcome.PlayerId;
            endpoint.Close();
            host.ProcessInboundOnce();
            host.AdvanceTicks(10);
        }

        (_, Welcome finalWelcome, EnterZoneAck finalAck) = ConnectAndEnter(host, "alice", 1);
        playerId ??= finalWelcome.PlayerId;

        Assert.True(host.TryGetPlayerState(playerId.Value, out PlayerState state));
        Assert.Equal(finalAck.EntityId, state.EntityId);
        Assert.True(host.WorldContainsEntity(finalAck.EntityId));
        Assert.Equal(1, host.CountWorldEntitiesForPlayer(playerId.Value));
    }

    [Fact]
    public void DisconnectedEntity_Dies_ReconnectRespawns()
    {
        ServerHost host = new(ServerConfig.Default(seed: 11) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 1,
            NpcCountPerZone = 12,
            DisconnectGraceTicks = 500
        });

        (InMemoryEndpoint first, Welcome welcome, EnterZoneAck firstAck) = ConnectAndEnter(host, "alice", 1);
        first.Close();
        host.ProcessInboundOnce();

        WaitForEntityDeath(host, welcome.PlayerId, maxTicks: 1200);

        (_, _, EnterZoneAck secondAck) = ConnectAndEnter(host, "alice", 1);

        Assert.NotEqual(firstAck.EntityId, secondAck.EntityId);
        Assert.False(host.WorldContainsEntity(firstAck.EntityId));
        Assert.True(host.WorldContainsEntity(secondAck.EntityId));
    }

    [Fact]
    public void SecondLogin_SameAccount_KicksOldSession()
    {
        ServerHost host = new(ServerConfig.Default(seed: 12) with { SnapshotEveryTicks = 1 });

        InMemoryEndpoint endpointA = new();
        host.Connect(endpointA);
        endpointA.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "alice")));
        host.ProcessInboundOnce();
        Welcome welcomeA = ReadSingle<Welcome>(endpointA);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.ProcessInboundOnce();
        EnterZoneAck ackA = ReadSingle<EnterZoneAck>(endpointA);

        InMemoryEndpoint endpointB = new();
        host.Connect(endpointB);
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "alice")));
        host.ProcessInboundOnce();
        Welcome welcomeB = ReadSingle<Welcome>(endpointB);

        endpointB.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.ProcessInboundOnce();
        EnterZoneAck ackB = ReadSingle<EnterZoneAck>(endpointB);

        host.AdvanceSimulationOnce();

        Assert.True(endpointA.IsClosed);
        Assert.Equal(welcomeA.PlayerId, welcomeB.PlayerId);
        Assert.Equal(ackA.EntityId, ackB.EntityId);
    }

    private static (InMemoryEndpoint Endpoint, Welcome Welcome, EnterZoneAck Ack) ConnectAndEnter(ServerHost host, string accountId, int zoneId)
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

    private static void WaitForEntityDeath(ServerHost host, PlayerId playerId, int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            host.AdvanceTicks(1);
            if (!host.TryGetPlayerState(playerId, out PlayerState state) || state.EntityId is null)
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("Player entity did not die within expected ticks.");
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
