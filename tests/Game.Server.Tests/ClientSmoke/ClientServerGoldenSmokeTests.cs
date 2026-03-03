using Game.Client.Headless;
using Game.Client.Headless.Runtime;
using Game.Core;
using Game.Server;
using System.Collections.Immutable;
using Xunit;

namespace Game.Server.Tests.ClientSmoke;

public sealed class ClientServerGoldenSmokeTests
{
    private const int RunTicks = 3600;
    private const string GoldenHash = "TODO_SET_FROM_CI";

    [Fact]
    [Trait("Category", "PR98")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public async Task ClientServer_Smoke60s_Arena_Golden()
    {
        ClientRunResult result = await RunArenaGoldenSmokeAsync(RunTicks);

        Assert.True(result.HandshakeOk);
        Assert.True(result.TicksProcessed >= RunTicks, $"Expected at least {RunTicks} ticks, got {result.TicksProcessed}.");
        Assert.True(result.HitEventsSeen > 0, "Expected at least one HitEvent during the deterministic smoke run.");
        Assert.False(string.IsNullOrWhiteSpace(result.TraceHash));

        if (GoldenHash == "TODO_SET_FROM_CI")
        {
            Assert.Fail($"GoldenHash placeholder detected. Set GoldenHash to the CI-produced TraceHash: {result.TraceHash}");
        }

        Assert.True(
            string.Equals(result.TraceHash, GoldenHash, StringComparison.Ordinal),
            $"TraceHash mismatch. Expected GoldenHash='{GoldenHash}', Actual TraceHash='{result.TraceHash}'.");
    }

    private static async Task<ClientRunResult> RunArenaGoldenSmokeAsync(int runTicks)
    {
        ServerConfig config = ServerConfig.Default(seed: 9801) with
        {
            SnapshotEveryTicks = 1,
            ArenaMode = true,
            MaxMoveSpeed = Fix32.Zero,
            VisionRadius = Fix32.FromInt(8),
            VisionRadiusSq = Fix32.FromInt(8) * Fix32.FromInt(8)
        };

        ServerBootstrap bootstrap = CreateDeterministicArenaBootstrap(config);
        ServerHost host = new(config, bootstrap: bootstrap);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        ClientOptions options = new("inproc", 0, 1, "basic", ArenaZoneFactory.ArenaZoneId, 1, "client-smoke-pr98", StopOnFirstHit: false);
        await using InMemoryClientTransport transport = new(endpoint);
        HeadlessClientRunner runner = new(transport, options);

        using CancellationTokenSource cts = new();
        Task<ClientRunResult> runTask = runner.RunAsync(maxTicks: runTicks, cts.Token);

        const int maxServerSteps = 20000;
        int serverSteps = 0;

        while (!runTask.IsCompleted)
        {
            if (serverSteps++ >= maxServerSteps)
            {
                cts.Cancel();
                throw new Xunit.Sdk.XunitException($"Client run did not complete within {maxServerSteps} deterministic server steps.");
            }

            host.ProcessInboundOnce();
            host.AdvanceSimulationOnce();
            await DrainClientOutboundQueueAsync(endpoint, runTask);

            const int maxInboundDrainSpins = 4096;
            int inboundDrainSpins = 0;
            while (!runTask.IsCompleted && endpoint.PendingToServerCount > 0)
            {
                if (inboundDrainSpins++ >= maxInboundDrainSpins)
                {
                    break;
                }

                host.ProcessInboundOnce();
                await DrainClientOutboundQueueAsync(endpoint, runTask);
            }

            await Task.Yield();
        }

        return await runTask;
    }

    private static async Task DrainClientOutboundQueueAsync(InMemoryEndpoint endpoint, Task<ClientRunResult> runTask)
    {
        const int maxDrainSpins = 4096;
        int spins = 0;
        while (!runTask.IsCompleted && endpoint.PendingToClientCount > 0)
        {
            if (spins++ >= maxDrainSpins)
            {
                break;
            }

            await Task.Yield();
        }
    }

    private static ServerBootstrap CreateDeterministicArenaBootstrap(ServerConfig config)
    {
        WorldState world = ArenaZoneFactory.CreateWorld(config.ToSimulationConfig());
        ZoneState zone = world.Zones.Single(z => z.Id.Value == ArenaZoneFactory.ArenaZoneId);

        Vec2Fix playerSpawn = ArenaZoneFactory.ResolvePlayerSpawnPoint(1);
        EntityState guaranteedTarget = new(
            new EntityId(1),
            new Vec2Fix(playerSpawn.X - Fix32.One, playerSpawn.Y),
            new Vec2Fix(Fix32.Zero, Fix32.Zero),
            100,
            100,
            true,
            Fix32.One,
            1,
            1,
            0,
            EntityKind.Player);

        ImmutableArray<EntityState> entities = ImmutableArray.Create(guaranteedTarget);
        ZoneState updatedZone = zone.WithEntities(entities);
        WorldState updatedWorld = world with
        {
            Zones = world.Zones.Select(z => z.Id.Value == ArenaZoneFactory.ArenaZoneId ? updatedZone : z).ToImmutableArray(),
            EntityLocations = entities.Select(e => new EntityLocation(e.Id, new ZoneId(ArenaZoneFactory.ArenaZoneId))).ToImmutableArray()
        };

        return new ServerBootstrap(updatedWorld, config.Seed, ImmutableArray<BootstrapPlayerRecord>.Empty);
    }
}
