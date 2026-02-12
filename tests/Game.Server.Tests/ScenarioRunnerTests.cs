using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests;

public sealed class ScenarioRunnerTests
{
    [Fact]
    public void ScenarioRunner_Baseline_IsDeterministic()
    {
        ScenarioConfig cfg = BaselineScenario.Config;

        string checksum1 = ScenarioRunner.Run(cfg);
        string checksum2 = ScenarioRunner.Run(cfg);

        Assert.Equal("63b5fe29bb2abc0eda67465f608382da5944461e0", checksum1);
        Assert.Equal(checksum1, checksum2);
    }

    [Fact]
    public void ScenarioRunner_BotsReceiveSnapshots_AndHaveEntityIds()
    {
        ScenarioConfig cfg = new(
            ServerSeed: 123,
            TickCount: 50,
            SnapshotEveryTicks: 1,
            BotCount: 2,
            ZoneId: 1,
            BaseBotSeed: 999);

        ScenarioResult result = ScenarioRunner.RunDetailed(cfg);

        Assert.All(result.BotStats, stats =>
        {
            Assert.NotNull(stats.EntityId);
            Assert.True(stats.SnapshotsReceived > 0);
        });
    }
}
