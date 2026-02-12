using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests;

public sealed class ScenarioRunnerTests
{
    // Baseline updated after deterministic harness stabilization.
    private const string BaselineChecksumPrefix = "2f21";

    [Fact]
    public void ScenarioRunner_Baseline_IsDeterministic()
    {
        ScenarioConfig cfg = BaselineScenario.Config;

        string checksum1 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));
        string checksum2 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));

        Assert.Equal(checksum1, checksum2);
        Assert.StartsWith(BaselineChecksumPrefix, checksum1, StringComparison.Ordinal);
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
