using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests;

public sealed class ScenarioRunnerTests
{
    [Fact]
    public void ScenarioRunner_Baseline_IsDeterministic()
    {
        ScenarioConfig cfg = new(
            ServerSeed: 123,
            TickCount: 500,
            SnapshotEveryTicks: 5,
            BotCount: 3,
            ZoneId: 1,
            BaseBotSeed: 999);

        string checksum1 = ScenarioRunner.Run(cfg);
        string checksum2 = ScenarioRunner.Run(cfg);

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
