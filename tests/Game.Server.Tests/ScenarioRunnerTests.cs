using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests;

public sealed class ScenarioRunnerTests
{
    [Fact]
    public void ScenarioRunner_Baseline_IsDeterministic()
    {
        ScenarioConfig cfg = BaselineScenario.Config;

        string checksum1 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));
        string checksum2 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));

        Assert.Equal(checksum1, checksum2);
        Assert.StartsWith(BaselineChecksums.ScenarioBaselinePrefix, checksum1, StringComparison.Ordinal);
    }

    [Fact]
    public void ScenarioRunner_ResultContainsSummaryAndMetrics()
    {
        ScenarioConfig cfg = new(
            ServerSeed: 123,
            TickCount: 50,
            SnapshotEveryTicks: 1,
            BotCount: 2,
            ZoneId: 1,
            BaseBotSeed: 999);

        ScenarioResult result = new ScenarioRunner().RunDetailed(cfg);

        Assert.True(result.MessagesOut > 0);
        Assert.True(result.MessagesIn > 0);
        Assert.All(result.BotStats, stats => Assert.True(stats.SnapshotsReceived > 0));
        Assert.True(result.TickAvgMs > 0);
        Assert.True(result.TickP95Ms >= result.TickAvgMs);
        Assert.Equal(0, result.InvariantFailures);
    }

    [Fact]
    public void ScenarioRunner_MetricsSnapshotStable_Smoke()
    {
        ScenarioConfig cfg = new(
            ServerSeed: 124,
            TickCount: 20,
            SnapshotEveryTicks: 1,
            BotCount: 1,
            ZoneId: 1,
            BaseBotSeed: 99);

        ScenarioResult result = new ScenarioRunner().RunDetailed(cfg);

        Assert.True(result.PlayersConnectedMax >= 1);
        Assert.True(result.TickAvgMs > 0);
        Assert.True(result.MessagesIn > 0);
    }

    [Fact]
    public void ScenarioRunner_WithNpcs_IsDeterministic()
    {
        ScenarioConfig cfg = new(
            ServerSeed: 451,
            TickCount: 120,
            SnapshotEveryTicks: 2,
            BotCount: 1,
            ZoneId: 1,
            BaseBotSeed: 777,
            NpcCount: 5);

        string checksum1 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));
        string checksum2 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));

        Assert.Equal(checksum1, checksum2);
    }

}
