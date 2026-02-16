namespace Game.BotRunner;

public static class BaselineScenario
{
    public static readonly ScenarioConfig Config = new(
        ServerSeed: 123,
        TickCount: 500,
        SnapshotEveryTicks: 5,
        BotCount: 3,
        ZoneId: 1,
        BaseBotSeed: 999);

    public const string FixtureRelativePath = "tests/Fixtures/replay_baseline.bin";

    public static ScenarioConfig CreateStressPreset() => new(
        ServerSeed: 12345,
        TickCount: 2000,
        SnapshotEveryTicks: 10,
        BotCount: 10,
        ZoneId: 1,
        BaseBotSeed: 4242,
        ZoneCount: 2,
        NpcCount: 5,
        VisionRadiusTiles: 12);

    public static ScenarioConfig CreateSoakPreset() => new(
        ServerSeed: 12345,
        TickCount: 10_000,
        SnapshotEveryTicks: 10,
        BotCount: 50,
        ZoneId: 1,
        BaseBotSeed: 54_321,
        ZoneCount: 1,
        NpcCount: 0,
        VisionRadiusTiles: 12);
}
