using Microsoft.Data.Sqlite;

namespace Game.Persistence.Sqlite;

public interface ISqliteMigration
{
    int FromVersion { get; }

    int ToVersion { get; }

    void Apply(SqliteConnection connection, SqliteTransaction transaction);
}
