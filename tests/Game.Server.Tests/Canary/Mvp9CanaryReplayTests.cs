using System.Text.Json;
using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests.Canary;

public sealed class Mvp9CanaryReplayTests
{
    private const string ExpectedFinalGlobalChecksumPrefix = "1e537a73eb7364239d1cf89dc53e4bce";

    [Fact]
    [Trait("Category", "Canary")]
    public void Canary_Mvp9_Multizone_ReplayVerify_Passes_AndChecksumMatches()
    {
        ScenarioConfig scenario = LoadScenario();

        using MemoryStream replay = new();
        ScenarioRunner.RunAndRecord(scenario, replay);
        replay.Position = 0;

        ReplayExecutionResult result = ReplayRunner.RunReplayWithExpected(replay);

        string expectedReplayChecksum = TestChecksum.NormalizeFullHex(result.ExpectedChecksum!);
        string actualReplayChecksum = TestChecksum.NormalizeFullHex(result.Checksum);
        Assert.True(
            string.Equals(expectedReplayChecksum, actualReplayChecksum, StringComparison.Ordinal),
            $"ReplayVerify PASS expected but failed. expected_checksum={expectedReplayChecksum} actual_checksum={actualReplayChecksum} FirstDivergentTick=n/a");

        string actualFinalGlobalChecksum = result.FinalGlobalChecksum;
        Assert.StartsWith(
            ExpectedFinalGlobalChecksumPrefix,
            actualFinalGlobalChecksum,
            StringComparison.Ordinal);
    }

    private static ScenarioConfig LoadScenario()
    {
        string filePath = FindRepoFile("tests/Game.Server.Tests/Canary/Replays/mvp9_multizone_canary.json");
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
