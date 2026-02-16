using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class SnapshotSequencingTests
{
    [Fact]
    public void SnapshotSeq_Increments_Monotonically()
    {
        ServerHost host = new(ServerConfig.Default(seed: 1001) with { SnapshotEveryTicks = 1 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "seq-user")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
        host.AdvanceTicks(5);

        List<int> seqs = DrainSnapshotV2(endpoint).Select(s => s.SnapshotSeq).Distinct().Take(3).ToList();
        Assert.True(seqs.Count >= 3, "Expected at least 3 snapshot sequences.");
        Assert.Equal(new[] { 1, 2, 3 }, seqs);
    }

    [Fact]
    public void InvalidAck_IsIgnored_AndDoesNotBreakServer()
    {
        ServerHost host = new(ServerConfig.Default(seed: 1002) with { SnapshotEveryTicks = 1 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "ack-user")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 999_999)));

        Exception? ex = Record.Exception(() => host.AdvanceTicks(2));
        Assert.Null(ex);

        SnapshotV2 snapshot = Assert.Single(DrainSnapshotV2(endpoint).Take(1));
        Assert.Equal(1, snapshot.SnapshotSeq);
    }

    [Fact]
    public void MissingAck_TriggersResend_OfLastSnapshotSeq()
    {
        ServerHost host = new(ServerConfig.Default(seed: 1003) with { SnapshotEveryTicks = 1, SnapshotRetryLimit = 16 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "resend-user")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));

        host.StepOnce();
        SnapshotV2 first = Assert.Single(DrainSnapshotV2(endpoint));
        Assert.Equal(1, first.SnapshotSeq);

        // Ack only seq=1 so next seq can become the missing one.
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 1)));
        host.StepOnce();
        SnapshotV2 second = Assert.Single(DrainSnapshotV2(endpoint));
        Assert.Equal(2, second.SnapshotSeq);

        // No ack for seq=2, next tick must resend seq=2 before continuing.
        host.StepOnce();
        SnapshotV2[] thirdTick = DrainSnapshotV2(endpoint).ToArray();
        Assert.True(thirdTick.Length >= 2, "Expected resend + fresh snapshot in the same tick.");
        Assert.Equal(2, thirdTick[0].SnapshotSeq);
        Assert.Equal(3, thirdTick[1].SnapshotSeq);
    }

    [Fact]
    public void RetryLimit_Exceeded_DisconnectsSession()
    {
        ServerHost host = new(ServerConfig.Default(seed: 1004) with { SnapshotEveryTicks = 1, SnapshotRetryLimit = 1 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "retry-user")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));

        host.StepOnce();
        _ = DrainSnapshotV2(endpoint).ToArray();

        host.StepOnce();
        Assert.True(endpoint.IsClosed);
    }

    private static IEnumerable<SnapshotV2> DrainSnapshotV2(InMemoryEndpoint endpoint)
    {
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (!ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _))
            {
                continue;
            }

            if (message is SnapshotV2 snapshot)
            {
                yield return snapshot;
            }
        }
    }
}
