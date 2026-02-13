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

        Assert.False(string.IsNullOrWhiteSpace(replayResult.ExpectedChecksum));
        string expectedChecksum = TestChecksum.NormalizeFullHex(replayResult.ExpectedChecksum!);
        Assert.Equal(expectedChecksum, replayChecksum);
        Assert.StartsWith(BaselineChecksums.ScenarioBaselinePrefix, expectedChecksum, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_WithMissingFinalChecksum_LeavesExpectedChecksumEmpty()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        ReplayExecutionResult replayResult = await Task.Run(() =>
        {
            using Stream replayStream = OpenFixtureStream();
            byte[] bytes = ReadAllBytes(replayStream);
            byte[] trimmed = TrimFinalChecksumRecord(bytes);
            using MemoryStream trimmedStream = new(trimmed, writable: false);
            return ReplayRunner.RunReplayWithExpected(trimmedStream);
        }, cts.Token).WaitAsync(cts.Token);

        Assert.True(string.IsNullOrWhiteSpace(replayResult.ExpectedChecksum));
        string replayChecksum = TestChecksum.NormalizeFullHex(replayResult.Checksum);
        Assert.StartsWith(BaselineChecksums.ScenarioBaselinePrefix, replayChecksum, StringComparison.Ordinal);
    }

    private static byte[] TrimFinalChecksumRecord(byte[] replayBytes)
    {
        using MemoryStream stream = new(replayBytes, writable: false);
        using ReplayReader reader = new(stream);

        int expectedTicks = reader.Header.TickCount;
        for (int i = 0; i < expectedTicks; i++)
        {
            Assert.True(reader.TryReadNext(out ReplayEvent evt));
            Assert.Equal(ReplayRecordType.TickInputs, evt.RecordType);
        }

        long offsetAfterTicks = stream.Position;
        if (!reader.TryReadNext(out ReplayEvent tail))
        {
            return replayBytes;
        }

        Assert.Equal(ReplayRecordType.FinalChecksum, tail.RecordType);
        return replayBytes.AsSpan(0, (int)offsetAfterTicks).ToArray();
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
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
