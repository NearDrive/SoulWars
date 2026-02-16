using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Game.Persistence.Sqlite;

public static class SqliteMigrator
{
    public static void InitializeOrMigrate(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        using SqliteConnection connection = OpenConnection(dbPath);
        EnsureMetaTable(connection);

        int version = ReadSchemaVersion(connection);
        if (version > SqliteSchema.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"DB created by newer server version. schema_version={version}, supported={SqliteSchema.CurrentVersion}.");
        }

        while (version < SqliteSchema.CurrentVersion)
        {
            ISqliteMigration migration = SqliteSchema.Migrations
                .SingleOrDefault(m => m.FromVersion == version)
                ?? throw new InvalidOperationException($"Missing SQLite migration from version {version}.");


            if (migration.ToVersion != version + 1)
            {
                throw new InvalidOperationException(
                    $"Invalid SQLite migration step {migration.FromVersion}->{migration.ToVersion}. Expected {version}->{version + 1}.");
            }

            using SqliteTransaction transaction = connection.BeginTransaction();
            migration.Apply(connection, transaction);
            WriteSchemaVersion(connection, transaction, migration.ToVersion);
            transaction.Commit();

            version = migration.ToVersion;
        }

        if (version != SqliteSchema.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"SQLite migration finished at schema_version={version}, expected={SqliteSchema.CurrentVersion}.");
        }
    }

    private static SqliteConnection OpenConnection(string dbPath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default
        };

        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }

    private static void EnsureMetaTable(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);";
        command.ExecuteNonQuery();
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'schema_version';";
        object? value = command.ExecuteScalar();

        if (value is null || value is DBNull)
        {
            return 0;
        }

        string raw = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedVersion) || parsedVersion < 0)
        {
            throw new InvalidDataException($"Invalid meta.schema_version '{raw}'.");
        }

        return parsedVersion;
    }

    private static void WriteSchemaVersion(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO meta (key, value)
VALUES ('schema_version', $schemaVersion)
ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$schemaVersion", version.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
