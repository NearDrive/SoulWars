using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class HardeningFuzzTests
{
    [Fact]
    public void ProtocolCodec_Fuzz_DoesNotThrowOrHang()
    {
        SimRng rng = new(1337);

        for (int i = 0; i < 10_000; i++)
        {
            int len = rng.NextInt(0, 257);
            byte[] payload = new byte[len];
            for (int b = 0; b < payload.Length; b++)
            {
                payload[b] = (byte)rng.NextInt(0, 256);
            }

            bool ok = ProtocolCodec.TryDecodeClient(payload, out IClientMessage? msg, out _);
            if (ok)
            {
                byte[] encoded = ProtocolCodec.Encode(msg!);
                Assert.NotNull(encoded);
            }
        }
    }


    [Fact]
    public void ProtocolCodec_KnownMessage_WithExtraTail_DoesNotThrow()
    {
        byte[] payload = ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "tail-test"));
        byte[] withTail = new byte[payload.Length + 3];
        payload.CopyTo(withTail, 0);
        withTail[^3] = 0xAA;
        withTail[^2] = 0xBB;
        withTail[^1] = 0xCC;

        Exception? ex = Record.Exception(() => ProtocolCodec.TryDecodeClient(withTail, out _, out _));

        Assert.Null(ex);
    }

    [Fact]
    public void TruncatedPayload_DisconnectsWithDecodeError()
    {
        ServerHost host = new(ServerConfig.Default(seed: 777));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        byte[] handshake = ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "decode-error"));
        byte[] truncated = handshake.Take(handshake.Length - 1).ToArray();
        endpoint.EnqueueToServer(truncated);

        host.ProcessInboundOnce();

        Assert.True(endpoint.IsClosed);
        Assert.Equal(0, host.Metrics.PlayersConnected);

        Assert.True(endpoint.TryDequeueFromServer(out byte[] disconnectPayload));
        Assert.True(ProtocolCodec.TryDecodeServer(disconnectPayload, out IServerMessage? msg, out ProtocolErrorCode error));
        Assert.Equal(ProtocolErrorCode.None, error);
        Disconnect disconnect = Assert.IsType<Disconnect>(msg);
        Assert.Equal(DisconnectReason.DecodeError, disconnect.Reason);
    }

    [Fact]
    public void VersionMismatch_DoesNotKeepActiveSession()
    {
        ServerHost host = new(ServerConfig.Default(seed: 778));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(999, "mismatch")));

        host.ProcessInboundOnce();

        Assert.True(endpoint.IsClosed);
        Assert.Equal(0, host.Metrics.PlayersConnected);

        Assert.True(endpoint.TryDequeueFromServer(out byte[] disconnectPayload));
        Assert.True(ProtocolCodec.TryDecodeServer(disconnectPayload, out IServerMessage? msg, out ProtocolErrorCode error));
        Assert.Equal(ProtocolErrorCode.None, error);
        Disconnect disconnect = Assert.IsType<Disconnect>(msg);
        Assert.Equal(DisconnectReason.VersionMismatch, disconnect.Reason);
    }

    [Fact]
    public void Handshake_SendsWelcomeWithProtocolAndCapabilities()
    {
        ServerHost host = new(ServerConfig.Default(seed: 780));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "caps")));
        host.ProcessInboundOnce();

        Assert.True(endpoint.TryDequeueFromServer(out byte[] welcomePayload));
        Assert.True(ProtocolCodec.TryDecodeServer(welcomePayload, out IServerMessage? msg, out ProtocolErrorCode error));
        Assert.Equal(ProtocolErrorCode.None, error);

        Welcome welcome = Assert.IsType<Welcome>(msg);
        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, welcome.ProtocolVersion);
        Assert.Equal(ProtocolConstants.ServerCapabilities, welcome.ServerCapabilities);
    }

    [Fact]
    public void UnknownClientMessageType_IsIgnoredWithoutDisconnect()
    {
        ServerHost host = new(ServerConfig.Default(seed: 779));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(new byte[] { 250, 1, 2, 3, 4 });
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "ok")));

        host.ProcessInboundOnce();

        Assert.False(endpoint.IsClosed);
        Assert.Equal(1, host.Metrics.PlayersConnected);
        Assert.True(endpoint.TryDequeueFromServer(out byte[] welcomePayload));
        Assert.True(ProtocolCodec.TryDecodeServer(welcomePayload, out IServerMessage? msg, out _));
        _ = Assert.IsType<Welcome>(msg);
    }
    [Fact]
    public void FrameDecoder_Fuzz_FragmentedInput_NoCrash()
    {
        SimRng rng = new(1337);
        FrameDecoder decoder = new(TcpEndpoint.MaxFrameBytes);

        for (int i = 0; i < 20_000; i++)
        {
            int len = rng.NextInt(1, 8);
            byte[] fragment = new byte[len];
            for (int j = 0; j < len; j++)
            {
                fragment[j] = (byte)rng.NextInt(0, 256);
            }

            decoder.Push(fragment);
            if (decoder.IsClosed)
            {
                return;
            }

            while (decoder.TryDequeueFrame(out byte[] _))
            {
            }
        }
    }

    [Fact]
    public void ServerInboundPipeline_Fuzz_NoCrash_AndCountsDecodeErrors()
    {
        ServerHost host = new(ServerConfig.Default(seed: 123));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        SimRng rng = new(1337);
        for (int i = 0; i < 3_000; i++)
        {
            int len = rng.NextInt(0, 257);
            byte[] payload = new byte[len];
            for (int b = 0; b < payload.Length; b++)
            {
                payload[b] = (byte)rng.NextInt(0, 256);
            }

            endpoint.EnqueueToServer(payload);
        }

        host.AdvanceTicks(50);

        Assert.True(host.Metrics.ProtocolDecodeErrors > 0);
    }

    [Fact]
    public async Task OversizedFrame_DisconnectsSession()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await using ServerRuntime runtime = new();
        await runtime.StartAsync(ServerConfig.Default(seed: 1), IPAddress.Loopback, 0, cts.Token);

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, runtime.BoundPort, cts.Token);
        NetworkStream stream = client.GetStream();

        byte[] len = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, TcpEndpoint.MaxFrameBytes + 1);
        await stream.WriteAsync(len, cts.Token);
        await stream.FlushAsync(cts.Token);

        const int maxSteps = 50;
        for (int i = 0; i < maxSteps && runtime.Host.Metrics.PlayersConnected > 0; i++)
        {
            runtime.StepOnce();
            await Task.Yield();
        }

        Assert.Equal(0, runtime.Host.Metrics.PlayersConnected);

        int read = await stream.ReadAsync(new byte[1], cts.Token);
        Assert.Equal(0, read);
    }

    [Fact]
    public void InvalidMove_IsRejected_NotApplied()
    {
        ServerHost host = new(ServerConfig.Default(seed: 99) with { SnapshotEveryTicks = 1 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("x", "x")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.AdvanceTicks(2);

        Snapshot before = ReadLastSnapshot(endpoint);
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(before.Tick + 1, (sbyte)5, (sbyte)0)));
        host.AdvanceTicks(3);
        Snapshot after = ReadLastSnapshot(endpoint);

        SnapshotEntity beforeEntity = before.Entities.OrderBy(e => e.EntityId).First();
        SnapshotEntity afterEntity = after.Entities.Single(e => e.EntityId == beforeEntity.EntityId);

        Assert.Equal(beforeEntity.PosXRaw, afterEntity.PosXRaw);
        Assert.Equal(beforeEntity.PosYRaw, afterEntity.PosYRaw);
        Assert.True(host.Metrics.ProtocolDecodeErrors > 0);
    }


    [Fact]
    public void ProtocolCodec_DecodeSnapshot_InvalidEntityKind_ReturnsErrorWithoutThrowing()
    {
        byte[] payload = ProtocolCodec.Encode(new Snapshot(
            Tick: 1,
            ZoneId: 1,
            Entities: new[]
            {
                new SnapshotEntity(
                    EntityId: 1,
                    PosXRaw: 0,
                    PosYRaw: 0,
                    VelXRaw: 0,
                    VelYRaw: 0,
                    Hp: 100,
                    Kind: SnapshotEntityKind.Player)
            }));

        payload[13 + 24] = 77;

        bool ok = ProtocolCodec.TryDecodeServer(payload, out IServerMessage? decoded, out ProtocolErrorCode error);

        Assert.False(ok);
        Assert.Null(decoded);
        Assert.Equal(ProtocolErrorCode.ValueOutOfRange, error);
    }

    [Fact]
    public void ProtocolCodec_DecodeSnapshot_ValidEntityKind_Succeeds()
    {
        Snapshot expected = new(
            Tick: 9,
            ZoneId: 1,
            Entities: new[]
            {
                new SnapshotEntity(1, 10, 11, 1, -1, 88, SnapshotEntityKind.Npc)
            });

        byte[] payload = ProtocolCodec.Encode(expected);

        bool ok = ProtocolCodec.TryDecodeServer(payload, out IServerMessage? decoded, out ProtocolErrorCode error);

        Assert.True(ok);
        Assert.Equal(ProtocolErrorCode.None, error);

        Snapshot snapshot = Assert.IsType<Snapshot>(decoded);
        SnapshotEntity entity = Assert.Single(snapshot.Entities);
        Assert.Equal(SnapshotEntityKind.Npc, entity.Kind);
        Assert.Equal(88, entity.Hp);
    }

    [Fact]
    public void Server_Fuzz_InvalidCommands_AreIgnored_NoCrash()
    {
        ServerHost host = new(ServerConfig.Default(seed: 321) with { SnapshotEveryTicks = 1, ZoneCount = 2, NpcCountPerZone = 3 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("fuzz", "fuzz")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.AdvanceTicks(2);

        SimRng rng = new(1337);
        for (int i = 0; i < 1000; i++)
        {
            int kind = rng.NextInt(0, 4);
            switch (kind)
            {
                case 0:
                    endpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(i + 1, (sbyte)rng.NextInt(-10, 11), (sbyte)rng.NextInt(-10, 11))));
                    break;
                case 1:
                    endpoint.EnqueueToServer(ProtocolCodec.Encode(new AttackIntent(i + 1, rng.NextInt(-20, 200), rng.NextInt(-20, 200), rng.NextInt(-4, 8))));
                    break;
                case 2:
                    endpoint.EnqueueToServer(ProtocolCodec.Encode(new TeleportRequest(rng.NextInt(-4, 8))));
                    break;
                default:
                    byte[] junk = new byte[rng.NextInt(0, 32)];
                    for (int b = 0; b < junk.Length; b++)
                    {
                        junk[b] = (byte)rng.NextInt(0, 256);
                    }

                    endpoint.EnqueueToServer(junk);
                    break;
            }

            host.StepOnce();
        }

        host.AdvanceTicks(20);
        Assert.True(host.Metrics.ProtocolDecodeErrors > 0);
    }

    private static Snapshot ReadLastSnapshot(InMemoryEndpoint endpoint)
    {
        Snapshot? last = null;
        while (endpoint.TryDequeueFromServer(out byte[] msg))
        {
            if (ProtocolCodec.TryDecodeServer(msg, out IServerMessage? decoded, out _) && decoded is Snapshot s)
            {
                last = s;
            }
        }

        Assert.NotNull(last);
        return last!;
    }
}
