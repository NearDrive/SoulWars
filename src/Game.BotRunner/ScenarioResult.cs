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
    int InvariantFailures,
    Game.Server.PerfSnapshot? PerfSnapshot = null,
    SoakGuardSnapshot? GuardSnapshot = null,
    int ActiveSessions = 0,
    int WorldEntityCount = 0);

public sealed record SoakGuardSnapshot(
    int MaxInboundQueueLen,
    int MaxOutboundQueueLen,
    int MaxEntityCount,
    int MaxPendingWorldCommands,
    int MaxPendingAttackIntents,
    int Failures);
