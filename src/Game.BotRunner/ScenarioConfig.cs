namespace Game.BotRunner;

public sealed record ScenarioConfig(
    int ServerSeed,
    int TickCount,
    int SnapshotEveryTicks,
    int BotCount,
    int ZoneId,
    int BaseBotSeed,
    int NpcCount = 0,
    int VisionRadiusTiles = 12);
