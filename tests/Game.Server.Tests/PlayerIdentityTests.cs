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
    public void Reconnect_SameAccount_ReusesEntityId()
    {
        ServerHost host = new(ServerConfig.Default(seed: 8) with { SnapshotEveryTicks = 1 });

        InMemoryEndpoint first = new();
        host.Connect(first);
        first.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "alice")));
        host.ProcessInboundOnce();
        _ = ReadSingle<Welcome>(first);

        first.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.ProcessInboundOnce();
        EnterZoneAck firstAck = ReadSingle<EnterZoneAck>(first);

        first.Close();
        host.ProcessInboundOnce();

        InMemoryEndpoint second = new();
        host.Connect(second);
        second.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "alice")));
        host.ProcessInboundOnce();
        _ = ReadSingle<Welcome>(second);

        second.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.ProcessInboundOnce();
        EnterZoneAck secondAck = ReadSingle<EnterZoneAck>(second);

        Assert.Equal(firstAck.EntityId, secondAck.EntityId);
    }

    [Fact]
    public void SecondLogin_SameAccount_KicksOldSession()
    {
        ServerHost host = new(ServerConfig.Default(seed: 9) with { SnapshotEveryTicks = 1 });

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
