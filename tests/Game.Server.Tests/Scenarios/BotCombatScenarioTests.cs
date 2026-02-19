using Game.Server.Scenarios;
using Xunit;

namespace Game.Server.Tests.Scenarios;

public sealed class BotCombatScenarioTests
{
    [Fact]
    [Trait("Category", "Canary")]
    public void BotCombatScenario_ReplayStable_WithProjectiles()
    {
        BotCombatScenario.RunResult runA = BotCombatScenario.RunDeterministic();
        BotCombatScenario.RunResult runB = BotCombatScenario.RunDeterministic();

        Assert.Equal(runA.FinalGlobalChecksum, runB.FinalGlobalChecksum);
        Assert.Equal(runA.CombatEventsHash, runB.CombatEventsHash);
        Assert.Equal(runA.FinalPerZoneChecksums.ToArray(), runB.FinalPerZoneChecksums.ToArray());
        Assert.Equal(runA.DurationTicks, runB.DurationTicks);
    }

    [Fact]
    [Trait("Category", "Canary")]
    public void BotCombatScenario_ReplayStable_WithBudgets()
    {
        BotCombatScenario.RunResult runA = BotCombatScenario.RunDeterministic();
        BotCombatScenario.RunResult runB = BotCombatScenario.RunDeterministic();

        Assert.Equal(runA.FinalGlobalChecksum, runB.FinalGlobalChecksum);
        Assert.Equal(runA.CombatEventsHash, runB.CombatEventsHash);
    }


    [Fact]
    public void BotCombat_NoEntityDuplicationAcrossZones()
    {
        BotCombatScenario.RunResult run = BotCombatScenario.RunDeterministic();

        Assert.True(run.NoCrossZoneDuplicates);
        Assert.True(run.ExpectedEntityCountMaintained);
    }
}
