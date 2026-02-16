using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests;

public sealed class SoakRunnerTests
{
    [Fact]
    [Trait("Category", "Soak")]
    public void Soak_50Bots_10000Ticks_TwoRuns_SameChecksum()
    {
        ScenarioConfig cfg = BaselineScenario.CreateSoakPreset();

        ScenarioResult r1 = ScenarioRunner.RunDetailed(cfg);
        ScenarioResult r2 = ScenarioRunner.RunDetailed(cfg);

        Assert.Equal(TestChecksum.NormalizeFullHex(r1.Checksum), TestChecksum.NormalizeFullHex(r2.Checksum));
        Assert.Equal(0, r1.InvariantFailures);
        Assert.Equal(0, r2.InvariantFailures);

        Assert.NotNull(r1.GuardSnapshot);
        Assert.NotNull(r2.GuardSnapshot);
        Assert.Equal(0, r1.GuardSnapshot!.Failures);
        Assert.Equal(0, r2.GuardSnapshot!.Failures);

        Assert.Equal(cfg.BotCount, r1.ActiveSessions);
        Assert.Equal(cfg.BotCount, r2.ActiveSessions);
    }
}
