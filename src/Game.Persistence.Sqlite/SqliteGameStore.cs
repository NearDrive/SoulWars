using System.Collections.Immutable;
using System.Data;
using Game.Core;
using Game.Persistence;
using Microsoft.Data.Sqlite;

namespace Game.Persistence.Sqlite;

public sealed record PlayerRecord(string AccountId, int PlayerId, int? EntityId, int? ZoneId);

public sealed record LoadResult(WorldState World, int ServerSeed, IReadOnlyList<PlayerRecord> Players, string? ChecksumHex);

public sealed class SqliteGameStore
{
    private const string SchemaVersion = "1";
    private const string FormatVersion = "1";

    private readonly string _dbPath;

    public SqliteGameStore(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        _dbPath = dbPath;
    }

    public void InitializeSchema()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);");

        ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS world_snapshots (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    saved_at_tick INTEGER NOT NULL,
    world_blob BLOB NOT NULL,
    checksum TEXT NOT NULL
);");

        ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS players (
    account_id TEXT PRIMARY KEY,
    player_id INTEGER NOT NULL,
    entity_id INTEGER NULL,
    zone_id INTEGER NULL
);");

        string? existingVersion = ReadMetaValue(connection, transaction, "schema_version");
        if (existingVersion is null)
        {
            WriteMetaValue(connection, transaction, "schema_version", SchemaVersion);
        }
        else if (!string.Equals(existingVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported sqlite schema_version '{existingVersion}'. Expected '{SchemaVersion}'.");
        }

        transaction.Commit();
    }

    public void SaveWorld(WorldState world, int serverSeed, IReadOnlyList<PlayerRecord> players, string checksumHex)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksumHex);

        InitializeSchema();

        byte[] worldBlob = WorldStateSerializer.SaveToBytes(world);

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        ExecuteNonQuery(connection, transaction, @"
INSERT INTO world_snapshots (id, saved_at_tick, world_blob, checksum)
VALUES (1, $tick, $blob, $checksum)
ON CONFLICT(id) DO UPDATE SET
    saved_at_tick = excluded.saved_at_tick,
    world_blob = excluded.world_blob,
    checksum = excluded.checksum;",
            ("$tick", world.Tick),
            ("$blob", worldBlob),
            ("$checksum", checksumHex));

        ExecuteNonQuery(connection, transaction, "DELETE FROM players;");

        foreach (PlayerRecord player in players.OrderBy(p => p.PlayerId))
        {
            ExecuteNonQuery(connection, transaction, @"
INSERT INTO players (account_id, player_id, entity_id, zone_id)
VALUES ($accountId, $playerId, $entityId, $zoneId);",
                ("$accountId", player.AccountId),
                ("$playerId", player.PlayerId),
                ("$entityId", (object?)player.EntityId ?? DBNull.Value),
                ("$zoneId", (object?)player.ZoneId ?? DBNull.Value));
        }

        WriteMetaValue(connection, transaction, "world_id", "default");
        WriteMetaValue(connection, transaction, "format_version", FormatVersion);
        WriteMetaValue(connection, transaction, "server_seed", serverSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));

        string createdAtTick = ReadMetaValue(connection, transaction, "created_at_tick")
            ?? world.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture);
        WriteMetaValue(connection, transaction, "created_at_tick", createdAtTick);
        WriteMetaValue(connection, transaction, "saved_at_tick", world.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture));

        transaction.Commit();
    }

    public LoadResult LoadWorld()
    {
        if (!File.Exists(_dbPath))
        {
            throw new FileNotFoundException($"SQLite database file not found at '{_dbPath}'.", _dbPath);
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        string? schemaVersion = ReadMetaValue(connection, transaction, "schema_version");
        if (!string.Equals(schemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported sqlite schema_version '{schemaVersion ?? "<missing>"}'. Expected '{SchemaVersion}'.");
        }

        string? formatVersion = ReadMetaValue(connection, transaction, "format_version");
        if (!string.Equals(formatVersion, FormatVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported world format_version '{formatVersion ?? "<missing>"}'. Expected '{FormatVersion}'.");
        }

        (byte[] Blob, string Checksum) snapshot = ReadSnapshot(connection, transaction);

        WorldState world;
        try
        {
            world = WorldStateSerializer.LoadFromBytes(snapshot.Blob);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException("Failed to decode world_blob from SQLite snapshot.", ex);
        }

        string? seedRaw = ReadMetaValue(connection, transaction, "server_seed");
        if (!int.TryParse(seedRaw, out int serverSeed))
        {
            throw new InvalidDataException("Missing or invalid meta.server_seed.");
        }

        ImmutableArray<PlayerRecord> players = ReadPlayers(connection, transaction);
        transaction.Commit();

        return new LoadResult(world, serverSeed, players, snapshot.Checksum);
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default
        };

        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }

    private static (byte[] Blob, string Checksum) ReadSnapshot(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT world_blob, checksum FROM world_snapshots WHERE id = 1;";

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("No world snapshot found in SQLite database.");
        }

        byte[] worldBlob = (byte[])reader[0];
        string checksum = reader.GetString(1);
        return (worldBlob, checksum);
    }

    private static ImmutableArray<PlayerRecord> ReadPlayers(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT account_id, player_id, entity_id, zone_id FROM players ORDER BY player_id ASC;";

        using SqliteDataReader reader = command.ExecuteReader();
        ImmutableArray<PlayerRecord>.Builder players = ImmutableArray.CreateBuilder<PlayerRecord>();

        while (reader.Read())
        {
            string accountId = reader.GetString(0);
            int playerId = reader.GetInt32(1);
            int? entityId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            int? zoneId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            players.Add(new PlayerRecord(accountId, playerId, entityId, zoneId));
        }

        return players.MoveToImmutable();
    }

    private static string? ReadMetaValue(SqliteConnection connection, SqliteTransaction transaction, string key)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void WriteMetaValue(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        ExecuteNonQuery(connection, transaction, @"
INSERT INTO meta (key, value)
VALUES ($key, $value)
ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            ("$key", key),
            ("$value", value));
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        _ = command.ExecuteNonQuery();
    }
}
