namespace Game.BotRunner;

public sealed record BotScenarioStats(
    int BotIndex,
    int? SessionId,
    int? EntityId,
    int SnapshotsReceived);

public sealed record ScenarioResult(
    string Checksum,
    IReadOnlyList<BotScenarioStats> BotStats);
