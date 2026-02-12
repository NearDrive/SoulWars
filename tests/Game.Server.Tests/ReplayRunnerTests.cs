using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests;

public sealed class ReplayRunnerTests
{
    // Baseline updated after recent replay behavior changes.
    private const string BaselineChecksumPrefix = "532a458a572f4250b32b34198a612e4b29293567d";

    [Fact]
    public async Task Replay_Verify_BaselineFixture()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        ReplayExecutionResult replayResult = await Task.Run(() =>
        {
            using Stream replayStream = OpenFixtureStream();
            return ReplayRunner.RunReplayWithExpected(replayStream);
        }, cts.Token).WaitAsync(cts.Token);

        Assert.StartsWith(BaselineChecksumPrefix, replayResult.Checksum, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(replayResult.ExpectedChecksum))
        {
            Assert.StartsWith(BaselineChecksumPrefix, replayResult.ExpectedChecksum, StringComparison.Ordinal);
            Assert.Equal(replayResult.ExpectedChecksum, replayResult.Checksum);
            return;
        }

        string scenarioChecksum = await Task.Run(() => ScenarioRunner.Run(BaselineScenario.Config), cts.Token).WaitAsync(cts.Token);
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
