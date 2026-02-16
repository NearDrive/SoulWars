using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "Security")]
public sealed class SecurityHardeningTests
{
    [Fact]
    public void FloodProtection_Disconnects_WhenMsgsExceedLimit()
    {
        ServerConfig cfg = ServerConfig.Default(seed: 200) with { MaxMsgsPerTick = 4 };
        ServerHost host = new(cfg);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        for (int i = 0; i < 5; i++)
        {
            endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("client", $"acc-{i}")));
        }

        host.ProcessInboundOnce();

        Disconnect disconnect = ReadDisconnect(endpoint);
        Assert.Equal(DisconnectReason.RateLimitExceeded, disconnect.Reason);
    }

    [Fact]
    public void DenyList_Expires_ByTick()
    {
        DenyList denyList = new();
        denyList.Deny("k", untilTick: 15);

        Assert.True(denyList.IsDenied("k", currentTick: 14));

        denyList.CleanupExpired(currentTick: 15);
        Assert.False(denyList.IsDenied("k", currentTick: 15));
    }

    [Fact]
    public void ConnLimit_Rejected_NoSessionCreated()
    {
        ServerConfig cfg = ServerConfig.Default(seed: 201) with { MaxConcurrentSessions = 1 };
        ServerHost host = new(cfg);

        TestEndpoint first = new("shared");
        TestEndpoint second = new("other");

        bool firstAccepted = host.TryConnect(first, out _, out DisconnectReason? firstReason);
        bool secondAccepted = host.TryConnect(second, out _, out DisconnectReason? secondReason);

        Assert.True(firstAccepted);
        Assert.Null(firstReason);
        Assert.False(secondAccepted);
        Assert.Equal(DisconnectReason.ConnLimitExceeded, secondReason);
        Assert.Equal(1, host.ActiveSessionCount);
    }

    [Fact]
    public void Abuse_Strikes_Then_Denylist()
    {
        ServerConfig cfg = ServerConfig.Default(seed: 202) with
        {
            MaxMsgsPerTick = 1,
            AbuseStrikesToDeny = 3,
            AbuseWindowTicks = 100,
            DenyTicks = 50
        };

        ServerHost host = new(cfg);

        for (int i = 0; i < 3; i++)
        {
            TestEndpoint attacker = new("abuser");
            Assert.True(host.TryConnect(attacker, out _, out _));

            attacker.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "a")));
            attacker.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "a")));

            host.ProcessInboundOnce();
            Disconnect disconnect = ReadDisconnect(attacker);
            Assert.Equal(DisconnectReason.RateLimitExceeded, disconnect.Reason);
            host.AdvanceTicks(1);
        }

        TestEndpoint denied = new("abuser");
        bool accepted = host.TryConnect(denied, out _, out DisconnectReason? reason);

        Assert.False(accepted);
        Assert.Equal(DisconnectReason.DenyListed, reason);
        Assert.Equal(DisconnectReason.DenyListed, ReadDisconnect(denied).Reason);
    }

    [Fact]
    public void Hardening_DoesNotAffectChecksum()
    {
        ServerConfig off = ServerConfig.Default(seed: 203) with
        {
            MaxConcurrentSessions = int.MaxValue,
            MaxConnectionsPerIp = int.MaxValue,
            MaxMsgsPerTick = int.MaxValue,
            MaxBytesPerTick = int.MaxValue,
            AbuseStrikesToDeny = int.MaxValue,
            AbuseWindowTicks = int.MaxValue,
            DenyTicks = int.MaxValue
        };

        ServerConfig on = ServerConfig.Default(seed: 203);

        ServerHost hostOff = new(off);
        ServerHost hostOn = new(on);

        hostOff.AdvanceTicks(120);
        hostOn.AdvanceTicks(120);

        Assert.Equal(StateChecksum.Compute(hostOff.CurrentWorld), StateChecksum.Compute(hostOn.CurrentWorld));
    }

    private static Disconnect ReadDisconnect(TestEndpoint endpoint)
    {
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? msg, out ProtocolErrorCode err) &&
                err == ProtocolErrorCode.None &&
                msg is Disconnect disconnect)
            {
                return disconnect;
            }
        }

        throw new Xunit.Sdk.XunitException("Disconnect message was not found.");
    }

    private static Disconnect ReadDisconnect(InMemoryEndpoint endpoint)
    {
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? msg, out ProtocolErrorCode err) &&
                err == ProtocolErrorCode.None &&
                msg is Disconnect disconnect)
            {
                return disconnect;
            }
        }

        throw new Xunit.Sdk.XunitException("Disconnect message was not found.");
    }

    private sealed class TestEndpoint : IServerEndpoint, IClientEndpoint
    {
        private readonly Queue<byte[]> _toServer = new();
        private readonly Queue<byte[]> _toClient = new();

        public TestEndpoint(string endpointKey)
        {
            EndpointKey = endpointKey;
        }

        public string EndpointKey { get; }

        public bool IsClosed { get; private set; }

        public bool TryDequeueToServer(out byte[] msg)
        {
            if (_toServer.Count == 0)
            {
                msg = Array.Empty<byte>();
                return false;
            }

            msg = _toServer.Dequeue();
            return true;
        }

        public void EnqueueToClient(byte[] msg)
        {
            if (!IsClosed)
            {
                _toClient.Enqueue(msg);
            }
        }

        public void Close() => IsClosed = true;

        public void EnqueueToServer(byte[] msg)
        {
            if (!IsClosed)
            {
                _toServer.Enqueue(msg);
            }
        }

        public bool TryDequeueFromServer(out byte[] msg)
        {
            if (_toClient.Count == 0)
            {
                msg = Array.Empty<byte>();
                return false;
            }

            msg = _toClient.Dequeue();
            return true;
        }
    }
}
