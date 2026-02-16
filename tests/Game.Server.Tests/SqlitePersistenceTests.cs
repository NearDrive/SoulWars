using System.Globalization;
using Game.Core;
using Game.Persistence;
using Game.Persistence.Sqlite;
using Game.Protocol;
using Game.Server;
using Microsoft.Data.Sqlite;
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
    public void Sqlite_Migrate_V1_ToCurrent_LoadWorld_OK()
    {
        string dbPath = CreateTempDbPath();
        try
        {
            ServerHost host = new(ServerConfig.Default(seed: 411));
            host.AdvanceTicks(64);

            byte[] worldBlob = WorldStateSerializer.SaveToBytes(host.CurrentWorld);
            string expectedChecksum = StateChecksum.Compute(host.CurrentWorld);
            CreateV1Database(dbPath, worldBlob, expectedChecksum, serverSeed: 411, savedAtTick: host.CurrentWorld.Tick);

            SqliteMigrator.InitializeOrMigrate(dbPath);

            Assert.Equal(SqliteSchema.CurrentVersion, ReadSchemaVersion(dbPath));

            SqliteGameStore store = new(dbPath);
            LoadResult loaded = store.LoadWorld();
            string loadedChecksum = StateChecksum.Compute(loaded.World);

            Assert.Equal(expectedChecksum, loadedChecksum);
            Assert.Equal(411, loaded.ServerSeed);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void Sqlite_Migrate_IsIdempotent()
    {
        string dbPath = CreateTempDbPath();
        try
        {
            SqliteMigrator.InitializeOrMigrate(dbPath);
            SqliteMigrator.InitializeOrMigrate(dbPath);

            Assert.Equal(SqliteSchema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void Sqlite_FutureSchema_Throws()
    {
        string dbPath = CreateTempDbPath();
        try
        {
            using SqliteConnection connection = new($"Data Source={dbPath}");
            connection.Open();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
CREATE TABLE meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
INSERT INTO meta (key, value) VALUES ('schema_version', $version);";
                command.Parameters.AddWithValue("$version", (SqliteSchema.CurrentVersion + 1).ToString(CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
            }

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => SqliteMigrator.InitializeOrMigrate(dbPath));
            Assert.Contains("DB created by newer server version", ex.Message, StringComparison.Ordinal);
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

    private static void CreateV1Database(string dbPath, byte[] worldBlob, string checksum, int serverSeed, int savedAtTick)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();

        using SqliteTransaction transaction = connection.BeginTransaction();

        ExecuteNonQuery(connection, transaction, @"
CREATE TABLE meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);");

        ExecuteNonQuery(connection, transaction, @"
CREATE TABLE world_snapshots (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    saved_at_tick INTEGER NOT NULL,
    world_blob BLOB NOT NULL,
    checksum TEXT NOT NULL
);");

        ExecuteNonQuery(connection, transaction, @"
CREATE TABLE players (
    account_id TEXT PRIMARY KEY,
    player_id INTEGER NOT NULL,
    entity_id INTEGER NULL,
    zone_id INTEGER NULL
);");

        ExecuteNonQuery(connection, transaction,
            "INSERT INTO meta (key, value) VALUES ('schema_version', '1');");
        ExecuteNonQuery(connection, transaction,
            "INSERT INTO meta (key, value) VALUES ('format_version', '1');");
        ExecuteNonQuery(connection, transaction,
            "INSERT INTO meta (key, value) VALUES ('world_id', 'default');");

        ExecuteNonQuery(connection, transaction,
            "INSERT INTO meta (key, value) VALUES ('server_seed', $seed);",
            ("$seed", serverSeed.ToString(CultureInfo.InvariantCulture)));

        ExecuteNonQuery(connection, transaction,
            "INSERT INTO meta (key, value) VALUES ('created_at_tick', $tick);",
            ("$tick", savedAtTick.ToString(CultureInfo.InvariantCulture)));

        ExecuteNonQuery(connection, transaction,
            "INSERT INTO meta (key, value) VALUES ('saved_at_tick', $tick);",
            ("$tick", savedAtTick.ToString(CultureInfo.InvariantCulture)));

        ExecuteNonQuery(connection, transaction,
            "INSERT INTO world_snapshots (id, saved_at_tick, world_blob, checksum) VALUES (1, $tick, $blob, $checksum);",
            ("$tick", savedAtTick),
            ("$blob", worldBlob),
            ("$checksum", checksum));

        transaction.Commit();
    }

    private static int ReadSchemaVersion(string dbPath)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'schema_version';";
        string? versionRaw = command.ExecuteScalar() as string;

        Assert.NotNull(versionRaw);
        Assert.True(int.TryParse(versionRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version));
        return version;
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
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
