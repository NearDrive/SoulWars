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
}
