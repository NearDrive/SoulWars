using System.Text.Json;
using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests.Canary;

public sealed class FogTransitionGoldenReplayTests
{
    [Fact]
    [Trait("Category", "ReplayVerify")]
    [Trait("Category", "Canary")]
    public void ReplayVerify_FogTransitionScenario_Golden_IsDeterministic()
    {
        ScenarioConfig scenario = LoadScenario();

        using MemoryStream replayA = new();
        ScenarioRunner.RunAndRecord(scenario, replayA);
        replayA.Position = 0;
        ReplayExecutionResult runA = ReplayRunner.RunReplayWithExpected(replayA);

        using MemoryStream replayB = new();
        ScenarioRunner.RunAndRecord(scenario, replayB);
        replayB.Position = 0;
        ReplayExecutionResult runB = ReplayRunner.RunReplayWithExpected(replayB);

        Assert.Equal(runA.Checksum, runB.Checksum);
        Assert.Equal(runA.FinalGlobalChecksum, runB.FinalGlobalChecksum);
        Assert.Equal(
            runA.FinalZoneChecksums.OrderBy(z => z.ZoneId).Select(z => z.ZoneId),
            runA.FinalZoneChecksums.Select(z => z.ZoneId));
        Assert.False(string.IsNullOrWhiteSpace(runA.FinalGlobalChecksum));
        Assert.Equal(64, runA.FinalGlobalChecksum.Length);
    }

    private static ScenarioConfig LoadScenario()
    {
        string filePath = FindRepoFile("tests/Game.Server.Tests/Canary/Replays/fog_transition_scenario.json");
        string json = File.ReadAllText(filePath);
        ScenarioConfig? cfg = JsonSerializer.Deserialize<ScenarioConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return cfg ?? throw new InvalidDataException($"Failed to parse canary scenario JSON at '{filePath}'.");
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Unable to locate '{relativePath}' from AppContext.BaseDirectory.");
    }
}
