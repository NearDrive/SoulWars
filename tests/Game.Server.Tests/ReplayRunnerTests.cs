using System.Text.Json;
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
        Assert.StartsWith(BaselineChecksums.ReplayBaselinePrefix, replayChecksum, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(replayResult.ExpectedChecksum))
        {
            string expectedChecksum = TestChecksum.NormalizeFullHex(replayResult.ExpectedChecksum);
            Assert.Equal(expectedChecksum, replayChecksum);
            Assert.StartsWith(BaselineChecksums.ReplayBaselinePrefix, expectedChecksum, StringComparison.Ordinal);
            return;
        }

        ReplayExecutionResult replayResultSecond = await Task.Run(() =>
        {
            using Stream replayStream = OpenFixtureStream();
            return ReplayRunner.RunReplayWithExpected(replayStream);
        }, cts.Token).WaitAsync(cts.Token);

        string replayChecksumSecond = TestChecksum.NormalizeFullHex(replayResultSecond.Checksum);
        Assert.Equal(replayChecksum, replayChecksumSecond);
        Assert.StartsWith(BaselineChecksums.ReplayBaselinePrefix, replayChecksumSecond, StringComparison.Ordinal);
    }


    [Fact]
    public async Task ReplayVerify_Pass_EmitsNoArtifacts()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "soulwars-pr47-tests", nameof(ReplayVerify_Pass_EmitsNoArtifacts));
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }

        Directory.CreateDirectory(tempRoot);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
        ReplayExecutionResult replayResult = await Task.Run(() =>
        {
            using Stream replayStream = OpenFixtureStream();
            return ReplayRunner.RunReplayWithExpected(replayStream, verifyOptions: new ReplayVerifyOptions(tempRoot));
        }, cts.Token).WaitAsync(cts.Token);

        if (!string.IsNullOrWhiteSpace(replayResult.ExpectedChecksum))
        {
            Assert.Equal(TestChecksum.NormalizeFullHex(replayResult.ExpectedChecksum), TestChecksum.NormalizeFullHex(replayResult.Checksum));
        }
        else
        {
            ReplayExecutionResult replayResultSecond = await Task.Run(() =>
            {
                using Stream replayStream = OpenFixtureStream();
                return ReplayRunner.RunReplayWithExpected(replayStream, verifyOptions: new ReplayVerifyOptions(tempRoot));
            }, cts.Token).WaitAsync(cts.Token);

            Assert.Equal(TestChecksum.NormalizeFullHex(replayResult.Checksum), TestChecksum.NormalizeFullHex(replayResultSecond.Checksum));
        }

        Assert.Empty(Directory.GetFiles(tempRoot));
    }

    [Fact]
    public void ReplayVerify_Mismatch_EmitsArtifacts()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "soulwars-pr44-tests", nameof(ReplayVerify_Mismatch_EmitsArtifacts));
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }

        Directory.CreateDirectory(tempRoot);

        using Stream replayStream = CreateReplayWithWrongExpectedChecksum();
        ReplayVerificationException ex = Assert.Throws<ReplayVerificationException>(() =>
            ReplayRunner.RunReplayWithExpected(replayStream, verifyOptions: new ReplayVerifyOptions(tempRoot)));

        string expectedChecksumPath = Path.Combine(tempRoot, "expected_checksum.txt");
        string actualChecksumPath = Path.Combine(tempRoot, "actual_checksum.txt");
        string expectedTickReportPath = Path.Combine(tempRoot, "tickreport_expected.jsonl");
        string actualTickReportPath = Path.Combine(tempRoot, "tickreport_actual.jsonl");

        Assert.True(File.Exists(expectedChecksumPath));
        Assert.True(File.Exists(actualChecksumPath));
        Assert.True(File.Exists(expectedTickReportPath));
        Assert.True(File.Exists(actualTickReportPath));

        string expectedChecksum = File.ReadAllText(expectedChecksumPath).Trim();
        string actualChecksum = File.ReadAllText(actualChecksumPath).Trim();
        Assert.NotEqual(expectedChecksum, actualChecksum);

        string[] expectedLines = File.ReadAllLines(expectedTickReportPath);
        string[] actualLines = File.ReadAllLines(actualTickReportPath);

        Assert.NotEmpty(expectedLines);
        Assert.Equal(expectedLines.Length, actualLines.Length);

        using ReplayReader reader = new(OpenFixtureStream());
        Assert.Equal(reader.Header.TickCount, expectedLines.Length);

        int firstTick = ReadTickFromJsonlLine(expectedLines[0]);
        Assert.True(firstTick is 0 or 1, $"Unexpected first tick in tickreport: {firstTick}.");
        Assert.Contains("FirstDivergentTick=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ExpectedChecksumAtTick=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ActualChecksumAtTick=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("artifact_output_dir=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("expected_checksum=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("actual_checksum=", ex.Message, StringComparison.Ordinal);
        Assert.Equal(tempRoot, ex.ArtifactsDirectory);
    }

    [Fact]
    public void ReplayVerify_Mismatch_IncludesFirstDivergentTick_InMessage()
    {
        using Stream replayStream = CreateReplayWithWrongExpectedChecksum();

        ReplayVerificationException ex = Assert.Throws<ReplayVerificationException>(() =>
            ReplayRunner.RunReplayWithExpected(replayStream));

        Assert.Contains("FirstDivergentTick=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ExpectedChecksumAtTick=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ActualChecksumAtTick=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("expected_checksum=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("actual_checksum=", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayVerify_Mismatch_WritesMismatchSummaryFile()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "soulwars-pr47-tests", nameof(ReplayVerify_Mismatch_WritesMismatchSummaryFile));
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }

        Directory.CreateDirectory(tempRoot);

        using Stream replayStream = CreateReplayWithWrongExpectedChecksum();
        ReplayVerificationException ex = Assert.Throws<ReplayVerificationException>(() =>
            ReplayRunner.RunReplayWithExpected(replayStream, verifyOptions: new ReplayVerifyOptions(tempRoot)));

        string summaryPath = Path.Combine(tempRoot, "mismatch_summary.txt");
        Assert.True(File.Exists(summaryPath));

        string summaryText = File.ReadAllText(summaryPath);
        Assert.Contains("FirstDivergentTick=", summaryText, StringComparison.Ordinal);
        Assert.Contains("ExpectedChecksumAtTick=", summaryText, StringComparison.Ordinal);
        Assert.Contains("ActualChecksumAtTick=", summaryText, StringComparison.Ordinal);
        Assert.Contains("ExpectedFinalChecksum=", summaryText, StringComparison.Ordinal);
        Assert.Contains("ActualFinalChecksum=", summaryText, StringComparison.Ordinal);
        Assert.Contains($"FirstDivergentTick={ex.DivergentTick}", summaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayVerify_Mismatch_SummaryContainsDeterministicDiffs()
    {
        string rootA = Path.Combine(Path.GetTempPath(), "soulwars-pr47-tests", nameof(ReplayVerify_Mismatch_SummaryContainsDeterministicDiffs), "run-a");
        string rootB = Path.Combine(Path.GetTempPath(), "soulwars-pr47-tests", nameof(ReplayVerify_Mismatch_SummaryContainsDeterministicDiffs), "run-b");

        ResetDir(rootA);
        ResetDir(rootB);

        using (Stream replayStream = CreateReplayWithWrongExpectedChecksum())
        {
            Assert.Throws<ReplayVerificationException>(() =>
                ReplayRunner.RunReplayWithExpected(replayStream, verifyOptions: new ReplayVerifyOptions(rootA)));
        }

        using (Stream replayStream = CreateReplayWithWrongExpectedChecksum())
        {
            Assert.Throws<ReplayVerificationException>(() =>
                ReplayRunner.RunReplayWithExpected(replayStream, verifyOptions: new ReplayVerifyOptions(rootB)));
        }

        byte[] summaryA = File.ReadAllBytes(Path.Combine(rootA, "mismatch_summary.txt"));
        byte[] summaryB = File.ReadAllBytes(Path.Combine(rootB, "mismatch_summary.txt"));
        Assert.Equal(summaryA, summaryB);
    }

    private static int ReadTickFromJsonlLine(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        return doc.RootElement.GetProperty("Tick").GetInt32();
    }

    private static void ResetDir(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static Stream CreateReplayWithWrongExpectedChecksum()
    {
        using Stream source = OpenFixtureStream();
        using ReplayReader reader = new(source);
        MemoryStream output = new();
        using ReplayWriter writer = new(output, reader.Header);

        string wrongExpected = "0000000000000000000000000000000000000000000000000000000000000000";
        bool hasFinalChecksum = false;
        while (reader.TryReadNext(out ReplayEvent evt))
        {
            if (evt.RecordType == ReplayRecordType.TickInputs)
            {
                writer.WriteTickInputs(evt.Tick, evt.Moves.AsSpan());
                continue;
            }

            hasFinalChecksum = true;
            writer.WriteFinalChecksum(wrongExpected);
        }

        if (!hasFinalChecksum)
        {
            writer.WriteFinalChecksum(wrongExpected);
        }

        output.Position = 0;
        return output;
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
