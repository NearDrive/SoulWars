using Game.Persistence.Sqlite.Migrations;

namespace Game.Persistence.Sqlite;

public static class SqliteSchema
{
    public const int CurrentVersion = 2;

    public static readonly IReadOnlyList<ISqliteMigration> Migrations =
    [
        new V0_To_V1(),
        new V1_To_V2()
    ];
}
