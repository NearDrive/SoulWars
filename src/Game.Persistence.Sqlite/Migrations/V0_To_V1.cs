using Microsoft.Data.Sqlite;

namespace Game.Persistence.Sqlite.Migrations;

public sealed class V0_To_V1 : ISqliteMigration
{
    public int FromVersion => 0;

    public int ToVersion => 1;

    public void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
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
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
