using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests;

public sealed class ReplayRunnerTests
{
    // Baseline updated to compare consistent full-hex checksum format.
    private const string BaselineChecksumFullHex = "63b5fe29bb2abc0eda67465f608382da5944461e0e74d3b0d898889fc1da2f";
    private const string BaselineChecksumPrefix = "63b5fe29bb2abc0eda67465f608382da5944461e0";

    [Fact]
    public async Task Replay_Verify_BaselineFixture()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        ReplayExecutionResult replayResult = await Task.Run(() =>
        {
            using Stream replayStream = OpenFixtureStream();
            return ReplayRunner.RunReplayWithExpected(replayStream);
        }, cts.Token).WaitAsync(cts.Token);

        string replayChecksum = TestChecksum.NormalizeFullHex(replayResult.Checksum);
        Assert.StartsWith(BaselineChecksumPrefix, replayChecksum, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(replayResult.ExpectedChecksum))
        {
            string expectedChecksum = TestChecksum.NormalizeFullHex(replayResult.ExpectedChecksum);
            Assert.StartsWith(BaselineChecksumPrefix, expectedChecksum, StringComparison.Ordinal);
            Assert.Equal(expectedChecksum, replayChecksum);
            return;
        }

        string scenarioChecksum = TestChecksum.NormalizeFullHex(
            await Task.Run(() => ScenarioRunner.Run(BaselineScenario.Config), cts.Token).WaitAsync(cts.Token));

        Assert.Equal(BaselineChecksumFullHex, replayChecksum);
        Assert.Equal(scenarioChecksum, replayChecksum);
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
