using Microsoft.Data.Sqlite;

namespace Game.Persistence.Sqlite.Migrations;

public sealed class V1_To_V2 : ISqliteMigration
{
    public int FromVersion => 1;

    public int ToVersion => 2;

    public void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteNonQuery(connection, transaction, @"
CREATE TABLE world_snapshots_v2 (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    saved_at_tick INTEGER NOT NULL,
    world_blob BLOB NOT NULL,
    checksum TEXT NOT NULL
);");

        ExecuteNonQuery(connection, transaction, @"
INSERT INTO world_snapshots_v2 (saved_at_tick, world_blob, checksum)
SELECT saved_at_tick, world_blob, checksum
FROM world_snapshots;");

        ExecuteNonQuery(connection, transaction, "DROP TABLE world_snapshots;");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE world_snapshots_v2 RENAME TO world_snapshots;");

        ExecuteNonQuery(connection, transaction, @"
ALTER TABLE players
ADD COLUMN last_seen_tick INTEGER NULL;");

        ExecuteNonQuery(connection, transaction, @"
CREATE INDEX IF NOT EXISTS idx_world_snapshots_saved_at_tick
ON world_snapshots(saved_at_tick);");

        ExecuteNonQuery(connection, transaction, @"
CREATE INDEX IF NOT EXISTS idx_players_player_id
ON players(player_id);");
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
