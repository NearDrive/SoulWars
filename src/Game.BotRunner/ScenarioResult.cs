namespace Game.BotRunner;

public sealed record BotStats(
    int BotIndex,
    int SnapshotsReceived,
    int Errors);

public sealed record ScenarioResult(
    string Checksum,
    int Ticks,
    int Bots,
    long MessagesIn,
    long MessagesOut,
    double TickAvgMs,
    double TickP95Ms,
    int PlayersConnectedMax,
    BotStats[] BotStats,
    int InvariantFailures);
