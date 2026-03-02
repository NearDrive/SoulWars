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
        Task<HeadlessRunResult> runTask = runner.RunAsync(maxTicks: 120, cts.Token);

        while (!runTask.IsCompleted && !cts.IsCancellationRequested)
        {
            host.StepOnce();
            await Task.Delay(1, cts.Token);
        }

        HeadlessRunResult result = await runTask;
        Assert.True(result.HandshakeAccepted);
        Assert.True(result.HitObserved, string.Join('\n', result.Logs));
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
