using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "PR86")]
public sealed class ProtocolVersioningTests
{
    [Fact]
    public void Handshake_WithUnknownProtocolVersion_IsRejected_AndSessionClosed()
    {
        ServerHost host = new(ServerConfig.Default(seed: 8601));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion + 1, "version-mismatch")));
        host.ProcessInboundOnce();

        Disconnect disconnect = ReadSingleMessage<Disconnect>(endpoint);
        Assert.Equal(DisconnectReason.VersionMismatch, disconnect.Reason);
        Assert.True(endpoint.IsClosed);
        Assert.Equal(0, host.ActiveSessionCount);
    }

    [Fact]
    public void Handshake_WithProtocolV1_IsAccepted_AndWelcomeEchoesAcceptedVersion()
    {
        ServerHost host = new(ServerConfig.Default(seed: 8602));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "accepted-v1")));
        host.ProcessInboundOnce();

        Welcome welcome = ReadSingleMessage<Welcome>(endpoint);
        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, welcome.ProtocolVersion);
        Assert.False(endpoint.IsClosed);
        Assert.Equal(1, host.ActiveSessionCount);
    }

    private static T ReadSingleMessage<T>(InMemoryEndpoint endpoint)
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
