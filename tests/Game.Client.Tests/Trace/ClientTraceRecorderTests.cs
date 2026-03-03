using Game.Client.Headless;
using Game.Client.Headless.Diagnostics;
using Game.Client.Headless.Runtime;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Client.Tests.Trace;

[Trait("Category", "PR95")]
[Trait("Category", "ClientSmoke")]
[Trait("Category", "Canary")]
public sealed class ClientTraceRecorderTests
{
    [Fact]
    [Trait("Category", "PR95")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public async Task ClientTrace_IsDeterministicAcrossRuns()
    {
        ClientRunResult result1 = await RunArenaScriptAsync("trace-run");
        ClientRunResult result2 = await RunArenaScriptAsync("trace-run");

        Assert.Equal(result1.TraceHash, result2.TraceHash);
        Assert.Equal(result1.CanonicalTrace, result2.CanonicalTrace);
    }


    [Fact]
    [Trait("Category", "PR95")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public void ClientTrace_UsesWorldStateVisibleEntities_ForDeltaSnapshots()
    {
        ClientWorldView world = new();
        ClientTraceRecorder recorder = new();

        SnapshotV2 full = new(
            Tick: 1,
            ZoneId: 1,
            SnapshotSeq: 1,
            IsFull: true,
            Entities:
            [
                new SnapshotEntity(3, 0, 0),
                new SnapshotEntity(1, 0, 0)
            ]);

        world.ApplySnapshot(full);
        recorder.RecordTick(full, world.GetVisibleEntityIdsCanonical());

        SnapshotV2 delta = new(
            Tick: 2,
            ZoneId: 1,
            SnapshotSeq: 2,
            IsFull: false,
            Entities: Array.Empty<SnapshotEntity>(),
            Leaves: [3],
            Enters: [new SnapshotEntity(2, 0, 0)],
            Updates: Array.Empty<SnapshotEntity>());

        world.ApplySnapshot(delta);
        recorder.RecordTick(delta, world.GetVisibleEntityIdsCanonical());

        string[] lines = recorder.BuildCanonicalTraceDump().Split('\n');
        Assert.Equal("T:2|Z:1|E:1,2|EV:2:1:-:-:-,3:2:-:-:-", lines[1]);
    }

    [Fact]
    [Trait("Category", "PR95")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public void ClientTrace_IsCanonicalOrdered()
    {
        ClientTraceRecorder recorder = new();
        SnapshotV2 snapshot = new(
            Tick: 15,
            ZoneId: 1,
            SnapshotSeq: 9,
            IsFull: true,
            Entities:
            [
                new SnapshotEntity(7, 0, 0),
                new SnapshotEntity(1, 0, 0),
                new SnapshotEntity(2, 0, 0)
            ],
            Leaves: [9, 3],
            Enters:
            [
                new SnapshotEntity(8, 0, 0),
                new SnapshotEntity(4, 0, 0)
            ],
            HitEvents:
            [
                new HitEventV1(TickId: 15, ZoneId: 1, SourceEntityId: 7, TargetEntityId: 2, AbilityId: 1, HitPosXRaw: 0, HitPosYRaw: 0, EventSeq: 2),
                new HitEventV1(TickId: 15, ZoneId: 1, SourceEntityId: 7, TargetEntityId: 1, AbilityId: 1, HitPosXRaw: 0, HitPosYRaw: 0, EventSeq: 1)
            ]);

        recorder.RecordTick(snapshot, new[] { 7, 1, 2 });

        string canonical = recorder.BuildCanonicalTraceDump();
        Assert.Equal("T:15|Z:1|E:1,2,7|EV:3:2:-:-:-,4:1:-:-:-,7:3:1:-:1,7:3:2:-:1,8:1:-:-:-,9:2:-:-:-", canonical);
    }

    private static async Task<ClientRunResult> RunArenaScriptAsync(string accountId)
    {
        ServerConfig config = ServerConfig.Default(seed: 9501) with
        {
            SnapshotEveryTicks = 1,
            ArenaMode = true,
            VisionRadius = Fix32.FromInt(8),
            VisionRadiusSq = Fix32.FromInt(8) * Fix32.FromInt(8)
        };

        ServerHost host = new(config);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        ClientOptions options = new("inproc", 0, 1, "basic", ArenaZoneFactory.ArenaZoneId, 1, accountId);
        await using InMemoryClientTransport transport = new(endpoint);
        HeadlessClientRunner runner = new(transport, options);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
        Task<ClientRunResult> runTask = runner.RunAsync(maxTicks: 120, cts.Token);

        while (!runTask.IsCompleted && !cts.IsCancellationRequested)
        {
            host.StepOnce();
            await Task.Delay(1, cts.Token);
        }

        return await runTask;
    }

    private sealed class InMemoryClientTransport : IClientTransport
    {
        private readonly InMemoryEndpoint _endpoint;

        public InMemoryClientTransport(InMemoryEndpoint endpoint)
        {
            _endpoint = endpoint;
        }

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Send(byte[] payload) => _endpoint.EnqueueToServer(payload);

        public bool TryRead(out byte[] payload) => _endpoint.TryDequeueFromServer(out payload!);

        public ValueTask DisposeAsync()
        {
            _endpoint.Close();
            return ValueTask.CompletedTask;
        }
    }
}
