using Game.Client.Headless.Runtime;
using System.Globalization;
using System.Linq;
using Game.Client.Headless;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests.ClientSmoke;

[Trait("Category", "ClientSmoke")]
public sealed class ClientMvpC1Tests
{
    [Fact]
    [Trait("Category", "PR91")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public void ClientHandshake_AcceptsV1()
    {
        ServerHost host = new(ServerConfig.Default(seed: 9101));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "client-smoke-v1")));
        host.ProcessInboundOnce();

        Welcome welcome = ReadSingle<Welcome>(endpoint);
        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, welcome.ProtocolVersion);
    }

    [Fact]
    [Trait("Category", "PR91")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public void ClientHandshake_RejectsUnknownVersion()
    {
        ServerHost host = new(ServerConfig.Default(seed: 9102));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(999, "client-smoke-bad-version")));
        host.ProcessInboundOnce();

        Disconnect disconnect = ReadSingle<Disconnect>(endpoint);
        Assert.Equal(DisconnectReason.VersionMismatch, disconnect.Reason);
    }

    [Fact]
    [Trait("Category", "PR92")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public void ClientStateDump_IsCanonical()
    {
        ClientWorldView view = new();
        SnapshotV2 snapshot = new(
            Tick: 5,
            ZoneId: 1,
            SnapshotSeq: 10,
            IsFull: true,
            Entities:
            [
                new SnapshotEntity(9, 200, 100, Hp: 99, Kind: SnapshotEntityKind.Npc),
                new SnapshotEntity(2, 12, 8, Hp: 10, Kind: SnapshotEntityKind.Player),
                new SnapshotEntity(5, 16, 9, Hp: 17, Kind: SnapshotEntityKind.Player)
            ]);

        view.ApplySnapshot(snapshot);
        string actual = view.DumpCanonical();

        string expected = string.Join('\n',
            "tick=5 zone=1",
            "entity=2 kind=Player pos=(12,8) hp=10",
            "entity=5 kind=Player pos=(16,9) hp=17",
            "entity=9 kind=Npc pos=(200,100) hp=99");

        Assert.Equal(expected, actual);
    }


    [Fact]
    [Trait("Category", "PR92")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public void ClientStateDump_IsCultureInvariant()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");

            ClientWorldView view = new();
            SnapshotV2 snapshot = new(
                Tick: 5,
                ZoneId: 1,
                SnapshotSeq: 10,
                IsFull: true,
                Entities:
                [
                    new SnapshotEntity(9, 200, 100, Hp: 99, Kind: SnapshotEntityKind.Npc),
                    new SnapshotEntity(2, 12, 8, Hp: 10, Kind: SnapshotEntityKind.Player),
                    new SnapshotEntity(5, 16, 9, Hp: 17, Kind: SnapshotEntityKind.Player)
                ]);

            view.ApplySnapshot(snapshot);
            string actual = view.DumpCanonical();

            string expected = string.Join('\n',
                "tick=5 zone=1",
                "entity=2 kind=Player pos=(12,8) hp=10",
                "entity=5 kind=Player pos=(16,9) hp=17",
                "entity=9 kind=Npc pos=(200,100) hp=99");

            Assert.Equal(expected, actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    [Trait("Category", "PR94")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public async Task ClientServer_Smoke_Arena_BasicRun()
    {
        ServerConfig config = ServerConfig.Default(seed: 9104) with
        {
            SnapshotEveryTicks = 1,
            ArenaMode = true,
            VisionRadius = Fix32.FromInt(8),
            VisionRadiusSq = Fix32.FromInt(8) * Fix32.FromInt(8)
        };

        ServerHost host = new(config);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        ClientOptions options = new("inproc", 0, 1, "basic", ArenaZoneFactory.ArenaZoneId, 1, "client-smoke-runner");
        await using InMemoryClientTransport transport = new(endpoint);
        HeadlessClientRunner runner = new(transport, options);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
        Task<ClientRunResult> runTask = runner.RunAsync(maxTicks: 120, cts.Token);

        while (!runTask.IsCompleted && !cts.IsCancellationRequested)
        {
            host.StepOnce();
            await Task.Yield();
        }

        ClientRunResult result = await runTask;
        Assert.True(result.HandshakeAccepted);
        Assert.NotEmpty(result.SentInputs);
        Assert.True(result.SentInputs.Select(input => input.Tick).SequenceEqual(result.SentInputs.Select(input => input.Tick).OrderBy(tick => tick)));
        Assert.True(result.SentInputs.Select(input => input.Tick).Distinct().Count() == result.SentInputs.Count);

        Game.Protocol.CastSkillCommand cast = Assert.Single(result.SentCasts);
        Assert.Equal(3, cast.TargetKind);
        Assert.Equal(0, cast.TargetEntityId);

        Assert.NotEmpty(result.ObservedHits);
        HitEventV1 hit = result.ObservedHits
            .OrderBy(evt => evt.TickId)
            .ThenBy(evt => evt.EventSeq)
            .First();
        Assert.Equal(options.AbilityId, hit.AbilityId);
    }

    [Fact]
    [Trait("Category", "PR96")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public async Task ClientHandlesPartialReads()
    {
        int[] chunkPattern = [1, 2, 7, 3, 16, 1, 1, 8, 5, 2, 11];

        ClientRunResult normal = await RunArenaScenarioAsync(endpoint => new InMemoryClientTransport(endpoint));
        ClientRunResult chunked = await RunArenaScenarioAsync(endpoint =>
            new ChunkingClientTransport(new InMemoryClientTransport(endpoint), chunkPattern));

        Assert.Equal(normal.TraceHash, chunked.TraceHash);
        Assert.Equal(normal.CanonicalTrace, chunked.CanonicalTrace);
    }

    [Fact]
    [Trait("Category", "PR96")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public async Task ChunkingTransport_IsDeterministic()
    {
        byte[] payload = Enumerable.Range(1, 24).Select(static value => (byte)value).ToArray();
        int[] chunkPattern = [1, 2, 7, 3, 16, 1];

        int[] first = await ReadChunkSizesAsync(payload, chunkPattern);
        int[] second = await ReadChunkSizesAsync(payload, chunkPattern);

        Assert.Equal(new[] { 1, 2, 7, 3, 11 }, first);
        Assert.Equal(first, second);
    }

    private static async Task<int[]> ReadChunkSizesAsync(byte[] payload, int[] chunkPattern)
    {
        await using ChunkingClientTransport chunked = new(
            new SinglePayloadTransport(payload),
            chunkPattern);

        List<int> sizes = new();
        while (chunked.TryRead(out byte[] chunk))
        {
            sizes.Add(chunk.Length);
        }

        return sizes.ToArray();
    }

    private static async Task<ClientRunResult> RunArenaScenarioAsync(Func<InMemoryEndpoint, IClientTransport> createTransport)
    {
        ServerConfig config = ServerConfig.Default(seed: 9106) with
        {
            SnapshotEveryTicks = 1,
            ArenaMode = true,
            VisionRadius = Fix32.FromInt(8),
            VisionRadiusSq = Fix32.FromInt(8) * Fix32.FromInt(8)
        };

        ServerHost host = new(config);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        ClientOptions options = new("inproc", 0, 1, "basic", ArenaZoneFactory.ArenaZoneId, 1, "client-smoke-pr96");
        await using IClientTransport transport = createTransport(endpoint);
        HeadlessClientRunner runner = new(transport, options);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
        Task<ClientRunResult> runTask = runner.RunAsync(maxTicks: 120, cts.Token);

        while (!runTask.IsCompleted && !cts.IsCancellationRequested)
        {
            host.ProcessInboundOnce();
            host.AdvanceSimulationOnce();
            await DrainClientOutboundQueueAsync(endpoint, runTask, cts.Token);

            const int maxInboundDrainSpins = 64;
            int inboundDrainSpins = 0;
            while (!runTask.IsCompleted && endpoint.PendingToServerCount > 0 && !cts.IsCancellationRequested)
            {
                if (inboundDrainSpins++ >= maxInboundDrainSpins)
                {
                    break;
                }

                host.ProcessInboundOnce();
                await DrainClientOutboundQueueAsync(endpoint, runTask, cts.Token);
            }

            await Task.Yield();
        }

        ClientRunResult result = await runTask;
        Assert.True(result.HandshakeAccepted);
        Assert.NotEmpty(result.CanonicalTrace);
        return result;
    }

    private static async Task DrainClientOutboundQueueAsync(InMemoryEndpoint endpoint, Task<ClientRunResult> runTask, CancellationToken cancellationToken)
    {
        const int maxDrainSpins = 64;
        int spins = 0;
        while (!runTask.IsCompleted && endpoint.PendingToClientCount > 0 && !cancellationToken.IsCancellationRequested)
        {
            if (spins++ >= maxDrainSpins)
            {
                break;
            }

            await Task.Yield();
        }
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

internal sealed class SinglePayloadTransport : IClientTransport
{
    private readonly Queue<byte[]> _payloads;

    public SinglePayloadTransport(byte[] payload)
    {
        _payloads = new Queue<byte[]>([payload]);
    }

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken) => Task.CompletedTask;

    public void Send(byte[] payload)
    {
    }

    public bool TryRead(out byte[] payload)
    {
        if (_payloads.Count == 0)
        {
            payload = Array.Empty<byte>();
            return false;
        }

        payload = _payloads.Dequeue();
        return true;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
