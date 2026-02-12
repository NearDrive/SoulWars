using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests;

public sealed class ReplayRunnerTests
{
    [Fact(Timeout = 20_000)]
    public void Replay_Verify_BaselineFixture()
    {
        using Stream replayStream = OpenFixtureStream();
        ReplayExecutionResult replayResult = ReplayRunner.RunReplayWithExpected(replayStream);

        if (!string.IsNullOrWhiteSpace(replayResult.ExpectedChecksum))
        {
            Assert.Equal(replayResult.ExpectedChecksum, replayResult.Checksum);
            return;
        }

        string scenarioChecksum = ScenarioRunner.Run(BaselineScenario.Config);
        Assert.Equal(scenarioChecksum, replayResult.Checksum);
    }

    private static Stream OpenFixtureStream()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string fixtures = Path.Combine(current.FullName, "tests", "Fixtures");
            string binaryCandidate = Path.Combine(fixtures, "replay_baseline.bin");
            if (File.Exists(binaryCandidate))
            {
                return File.OpenRead(binaryCandidate);
            }

            string hexCandidate = Path.Combine(fixtures, "replay_baseline.hex");
            if (File.Exists(hexCandidate))
            {
                string hex = File.ReadAllText(hexCandidate).Trim();
                byte[] bytes = Convert.FromHexString(hex);
                return new MemoryStream(bytes, writable: false);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Unable to locate tests/Fixtures/replay_baseline.bin or replay_baseline.hex from AppContext.BaseDirectory.");
    }
}
