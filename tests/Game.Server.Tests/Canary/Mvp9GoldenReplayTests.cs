using System.Text.Json;
using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests.Canary;

public sealed class Mvp9GoldenReplayTests
{
    private const string ExpectedFinalGlobalChecksum = "3eaf0f0f8a7b2f6ef43b48f95f6f2e7d8d1f7f861f7e0dcfcb1f8f5a6ce2a5c1";

    [Fact]
    [Trait("Category", "ReplayVerify")]
    public void ReplayVerify_Mvp9MultiZone_Canary_Golden()
    {
        ScenarioConfig scenario = LoadScenario();

        using MemoryStream replay = new();
        ScenarioRunner.RunAndRecord(scenario, replay);
        replay.Position = 0;

        ReplayExecutionResult result = ReplayRunner.RunReplayWithExpected(replay);

        Assert.False(string.IsNullOrWhiteSpace(result.FinalGlobalChecksum),
            $"Missing final global checksum. tick={result.FinalTick} zoneChecksums=[{FormatZoneChecksums(result.FinalZoneChecksums)}]");
        Assert.Equal(
            result.FinalZoneChecksums.OrderBy(z => z.ZoneId).Select(z => z.ZoneId),
            result.FinalZoneChecksums.Select(z => z.ZoneId));
        Assert.Equal(ExpectedFinalGlobalChecksum, result.FinalGlobalChecksum);
    }

    private static ScenarioConfig LoadScenario()
    {
        string filePath = FindRepoFile("tests/Game.Server.Tests/Canary/Replays/mvp9_multizone_golden.json");
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

    private static string FormatZoneChecksums(IReadOnlyList<Game.Core.ZoneChecksum> checksums)
        => string.Join(", ", checksums.OrderBy(z => z.ZoneId).Select(z => $"{z.ZoneId}:{z.Value}"));
}
