using System.Collections.Concurrent;
using System.Security.Cryptography;
using Game.BotRunner;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class MultiZoneSnapshotTests
{
    [Fact]
    public void Snapshots_MultiZone_HaveZoneIdAndCanonicalOrder()
    {
        List<OutboundSnapshotEvent> outbound = new();
        object sync = new();

        static void Capture(List<OutboundSnapshotEvent> output, object syncRoot, int sessionId, long order, byte[] payload)
        {
            if (!ProtocolCodec.TryDecodeServer(payload, out IServerMessage? msg, out _) || msg is null)
            {
                return;
            }

            if (msg is SnapshotV2 snapV2)
            {
                lock (syncRoot)
                {
                    output.Add(new OutboundSnapshotEvent(sessionId, order, snapV2.Tick, snapV2.ZoneId, snapV2.Entities.Select(e => e.EntityId).ToArray()));
                }

                return;
            }

            if (msg is Snapshot snap)
            {
                lock (syncRoot)
                {
                    output.Add(new OutboundSnapshotEvent(sessionId, order, snap.Tick, snap.ZoneId, snap.Entities.Select(e => e.EntityId).ToArray()));
                }
            }
        }

        ServerHost host = new(ServerConfig.Default(seed: 5101) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 2,
            NpcCountPerZone = 0
        });

        RecordingEndpoint zone1 = new("z1", (sid, seq, payload) => Capture(outbound, sync, sid, seq, payload));
        RecordingEndpoint zone2 = new("z2", (sid, seq, payload) => Capture(outbound, sync, sid, seq, payload));

        host.Connect(zone1);
        host.Connect(zone2);

        zone1.EnqueueToServer(ProtocolCodec.Encode(new Hello("v")));
        zone1.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));

        zone2.EnqueueToServer(ProtocolCodec.Encode(new Hello("v")));
        zone2.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(2)));

        host.AdvanceTicks(5);

        List<OutboundSnapshotEvent> events;
        lock (sync)
        {
            events = outbound
                .OrderBy(e => e.Order)
                .Where(e => e.Tick >= 2)
                .ToList();
        }

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.True(e.ZoneId is 1 or 2));

        foreach (IGrouping<int, OutboundSnapshotEvent> tickGroup in events.GroupBy(e => e.Tick))
        {
            int[] zoneOrder = tickGroup.Select(e => e.ZoneId).ToArray();
            Assert.Equal(zoneOrder.OrderBy(z => z).ToArray(), zoneOrder);
        }

        foreach (OutboundSnapshotEvent evt in events)
        {
            Assert.Equal(evt.EntityIds.OrderBy(id => id).ToArray(), evt.EntityIds);
        }
    }

    [Fact]
    public void Snapshots_DeterministicAcrossRuns()
    {
        string runA = RunAndHashSnapshots(seed: 5202);
        string runB = RunAndHashSnapshots(seed: 5202);

        Assert.Equal(runA, runB);
    }

    [Fact]
    public void ReplayVerify_MultiZone_Snapshots_Passes()
    {
        ScenarioConfig scenario = new(
            ServerSeed: 6060,
            TickCount: 32,
            SnapshotEveryTicks: 1,
            BotCount: 2,
            ZoneId: 1,
            BaseBotSeed: 7000,
            ZoneCount: 2,
            NpcCount: 0);

        using MemoryStream replay = new();
        ScenarioRunner.RunAndRecord(scenario, replay);
        replay.Position = 0;

        ReplayExecutionResult result = ReplayRunner.RunReplayWithExpected(replay);
        Assert.Equal(TestChecksum.NormalizeFullHex(result.ExpectedChecksum!), TestChecksum.NormalizeFullHex(result.Checksum));
    }

    private static string RunAndHashSnapshots(int seed)
    {
        ServerHost host = new(ServerConfig.Default(seed) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 2,
            NpcCountPerZone = 0
        });

        InMemoryEndpoint zone1 = new();
        InMemoryEndpoint zone2 = new();
        host.Connect(zone1);
        host.Connect(zone2);

        zone1.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "det-z1")));
        zone1.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
        zone1.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));

        zone2.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "det-z2")));
        zone2.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(2)));
        zone2.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(2, 0)));

        host.AdvanceTicks(8);

        List<byte> all = new();
        all.AddRange(DrainSnapshotPayloads(zone1));
        all.AddRange(DrainSnapshotPayloads(zone2));

        byte[] hash = SHA256.HashData(all.ToArray());
        return Convert.ToHexString(hash);
    }

    private static IEnumerable<byte> DrainSnapshotPayloads(InMemoryEndpoint endpoint)
    {
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (!ProtocolCodec.TryDecodeServer(payload, out IServerMessage? msg, out _) || msg is null)
            {
                continue;
            }

            if (msg is SnapshotV2)
            {
                foreach (byte b in payload)
                {
                    yield return b;
                }
            }
        }
    }

    private sealed record OutboundSnapshotEvent(int SessionId, long Order, int Tick, int ZoneId, int[] EntityIds);

    private sealed class RecordingEndpoint : IServerEndpoint, IClientEndpoint
    {
        private static long _order;
        private readonly ConcurrentQueue<byte[]> _toServer = new();
        private readonly ConcurrentQueue<byte[]> _toClient = new();
        private readonly Action<int, long, byte[]> _onOutbound;

        public RecordingEndpoint(string endpointKey, Action<int, long, byte[]> onOutbound)
        {
            EndpointKey = endpointKey;
            _onOutbound = onOutbound;
        }

        public string EndpointKey { get; }

        public bool IsClosed { get; private set; }

        public void EnqueueToServer(byte[] msg)
        {
            ArgumentNullException.ThrowIfNull(msg);
            if (!IsClosed)
            {
                _toServer.Enqueue(msg);
            }
        }

        public bool TryDequeueFromServer(out byte[] msg) => _toClient.TryDequeue(out msg!);

        public bool TryDequeueToServer(out byte[] msg) => _toServer.TryDequeue(out msg!);

        public void EnqueueToClient(byte[] msg)
        {
            ArgumentNullException.ThrowIfNull(msg);
            if (IsClosed)
            {
                return;
            }

            long seq = Interlocked.Increment(ref _order);
            _onOutbound(int.Parse(EndpointKey[1..], System.Globalization.CultureInfo.InvariantCulture), seq, msg);
            _toClient.Enqueue(msg);
        }

        public void Close() => IsClosed = true;
    }
}
