using Game.Core;
using Game.Persistence.Sqlite;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public void SqliteStore_SaveLoad_WorldBlob_RoundTrip()
    {
        string dbPath = CreateTempDbPath();
        try
        {
            ServerHost host = new(ServerConfig.Default(seed: 201));
            host.AdvanceTicks(120);

            SqliteGameStore store = new(dbPath);
            string expectedChecksum = StateChecksum.Compute(host.CurrentWorld);

            store.SaveWorld(
                host.CurrentWorld,
                serverSeed: 201,
                players: Array.Empty<PlayerRecord>(),
                checksumHex: expectedChecksum);

            LoadResult loaded = store.LoadWorld();
            string loadedChecksum = StateChecksum.Compute(loaded.World);

            Assert.Equal(expectedChecksum, loadedChecksum);
            Assert.Equal(201, loaded.ServerSeed);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void Server_SaveDb_Restart_LoadDb_Continue_EqualsBaseline()
    {
        const int seed = 303;
        const int totalTicks = 400;
        const int restartTick = 200;

        string baselineChecksum;
        {
            ServerHost baseline = new(ServerConfig.Default(seed));
            InMemoryEndpoint[] endpoints = ConnectTwoBots(baseline);
            RunDeterministicTicks(baseline, endpoints, 1, totalTicks);
            baselineChecksum = StateChecksum.Compute(baseline.CurrentWorld);
        }

        string dbPath = CreateTempDbPath();
        try
        {
            ServerConfig config = ServerConfig.Default(seed);
            ServerHost first = new(config);
            InMemoryEndpoint[] firstEndpoints = ConnectTwoBots(first);
            RunDeterministicTicks(first, firstEndpoints, 1, restartTick);
            first.SaveToSqlite(dbPath);

            ServerHost second = ServerHost.LoadFromSqlite(config, dbPath);
            InMemoryEndpoint[] secondEndpoints = ConnectTwoBots(second);
            RunDeterministicTicks(second, secondEndpoints, restartTick + 1, totalTicks);
            string restartChecksum = StateChecksum.Compute(second.CurrentWorld);

            Assert.Equal(baselineChecksum, restartChecksum);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void SqliteSchema_Initialize_IsIdempotent()
    {
        string dbPath = CreateTempDbPath();
        try
        {
            SqliteGameStore store = new(dbPath);
            store.InitializeSchema();
            store.InitializeSchema();
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    private static InMemoryEndpoint[] ConnectTwoBots(ServerHost host)
    {
        InMemoryEndpoint first = new();
        InMemoryEndpoint second = new();

        host.Connect(first);
        host.Connect(second);

        first.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("bot", "bot-a")));
        second.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("bot", "bot-b")));
        host.ProcessInboundOnce();

        first.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        second.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.ProcessInboundOnce();

        return [first, second];
    }

    private static void RunDeterministicTicks(ServerHost host, InMemoryEndpoint[] endpoints, int startTickInclusive, int endTickInclusive)
    {
        for (int tick = startTickInclusive; tick <= endTickInclusive; tick++)
        {
            for (int botIndex = 0; botIndex < endpoints.Length; botIndex++)
            {
                sbyte moveX = DeterministicAxis(tick, botIndex, salt: 17);
                sbyte moveY = DeterministicAxis(tick, botIndex, salt: 71);
                endpoints[botIndex].EnqueueToServer(ProtocolCodec.Encode(new InputCommand(tick, moveX, moveY)));
            }

            host.StepOnce();
        }
    }

    private static sbyte DeterministicAxis(int tick, int botIndex, int salt)
    {
        int value = ((tick * 31) + (botIndex * 17) + salt) % 3;
        return value switch
        {
            0 => (sbyte)-1,
            1 => (sbyte)0,
            _ => (sbyte)1
        };
    }

    private static string CreateTempDbPath()
    {
        string fileName = $"soulwars-{Guid.NewGuid():N}.sqlite";
        return Path.Combine(Path.GetTempPath(), fileName);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
