using Game.BotRunner;
using Xunit;

namespace Game.Server.Tests;

public sealed class ReplayRunnerTests
{
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
        Assert.StartsWith(BaselineChecksums.ScenarioBaselinePrefix, replayChecksum, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(replayResult.ExpectedChecksum))
        {
            string expectedChecksum = TestChecksum.NormalizeFullHex(replayResult.ExpectedChecksum);
            Assert.Equal(expectedChecksum, replayChecksum);
            Assert.StartsWith(BaselineChecksums.ScenarioBaselinePrefix, expectedChecksum, StringComparison.Ordinal);
            return;
        }

        string scenarioChecksum = TestChecksum.NormalizeFullHex(
            await Task.Run(() => ScenarioRunner.Run(BaselineScenario.Config), cts.Token).WaitAsync(cts.Token));

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

    [Fact]
    public void Replay_RoundTrip_WithNpcAttacks_PreservesChecksum()
    {
        ScenarioConfig cfg = new(
            ServerSeed: 451,
            TickCount: 220,
            SnapshotEveryTicks: 1,
            BotCount: 2,
            ZoneId: 1,
            BaseBotSeed: 777,
            NpcCount: 4);

        using MemoryStream replay = new();
        ScenarioResult scenarioResult = new ScenarioRunner().RunAndRecordDetailed(cfg, replay);
        replay.Position = 0;

        ReplayExecutionResult replayResult = ReplayRunner.RunReplayWithExpected(replay);

        Assert.Equal(
            TestChecksum.NormalizeFullHex(scenarioResult.Checksum),
            TestChecksum.NormalizeFullHex(replayResult.Checksum));
        Assert.Equal(
            TestChecksum.NormalizeFullHex(scenarioResult.Checksum),
            TestChecksum.NormalizeFullHex(replayResult.ExpectedChecksum));
    }
}
