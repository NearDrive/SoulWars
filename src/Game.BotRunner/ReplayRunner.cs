using Game.Protocol;
using Game.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Collections.Immutable;
using Game.Core;

namespace Game.BotRunner;

public static class ReplayRunner
{
    public static string RunReplay(Stream replayStream, ILoggerFactory? loggerFactory = null)
    {
        ReplayExecutionResult result = RunReplayInternal(replayStream, loggerFactory, verifyOptions: null, enforceExpectedChecksum: false);
        return result.Checksum;
    }

    public static ReplayExecutionResult RunReplayWithExpected(Stream replayStream, ILoggerFactory? loggerFactory = null, ReplayVerifyOptions? verifyOptions = null)
    {
        return RunReplayInternal(replayStream, loggerFactory, verifyOptions, enforceExpectedChecksum: true);
    }

    private static ReplayExecutionResult RunReplayInternal(
        Stream replayStream,
        ILoggerFactory? loggerFactory,
        ReplayVerifyOptions? verifyOptions,
        bool enforceExpectedChecksum)
    {
        ArgumentNullException.ThrowIfNull(replayStream);
        ILoggerFactory lf = loggerFactory ?? NullLoggerFactory.Instance;
        ILogger logger = lf.CreateLogger("ReplayRunner");

        using ReplayReader reader = new(replayStream);
        ReplayHeader header = reader.Header;

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
        bool captureTickReports = enforceExpectedChecksum;
        ServerConfig serverConfig = ServerConfig.Default(header.ServerSeed) with
        {
            SnapshotEveryTicks = header.SnapshotEveryTicks,
            EnableTickReports = captureTickReports
        };

        using ScenarioChecksumBuilder checksum = new();
        List<BotClient> clients = new(header.BotCount);
        Dictionary<(int BotIndex, int Tick), Snapshot> committedSnapshots = new();
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot = new();
        ServerHost host = new(serverConfig, lf);
        List<TickReport> actualTickReports = new();
        if (captureTickReports)
        {
            host.TickReportSink = actualTickReports.Add;
        }

        string? expectedChecksum = null;

        try
        {
            for (int i = 0; i < header.BotCount; i++)
            {
                InMemoryEndpoint endpoint = new();
                host.Connect(endpoint);

                BotClient client = new(i, header.ZoneId, endpoint);
                clients.Add(client);
                pendingSnapshotsByBot[i] = new SortedDictionary<int, Snapshot>();
            }

            ScenarioRunner.ConnectBotsForTests(host, clients, cts.Token);
            ScenarioRunner.DrainAllMessagesForTests(clients);

            for (int tick = 1; tick <= header.TickCount; tick++)
            {
                if (!reader.TryReadNext(out ReplayEvent evt))
                {
                    throw new InvalidDataException($"Replay ended before tick {tick}.");
                }

                if (evt.RecordType != ReplayRecordType.TickInputs)
                {
                    throw new InvalidDataException($"Expected TickInputs record for tick {tick}, got {evt.RecordType}.");
                }

                if (evt.Tick != tick)
                {
                    throw new InvalidDataException($"Replay tick mismatch: expected {tick}, got {evt.Tick}.");
                }

                if (evt.Moves.Length != header.BotCount)
                {
                    throw new InvalidDataException($"Replay move count mismatch for tick {tick}: expected {header.BotCount}, got {evt.Moves.Length}.");
                }

                for (int botIndex = 0; botIndex < header.BotCount; botIndex++)
                {
                    ReplayMove move = evt.Moves[botIndex];
                    clients[botIndex].SendInput(tick, move.MoveX, move.MoveY);
                }

                host.StepOnce();
                ScenarioRunner.DrainSnapshotsForTests(clients, pendingSnapshotsByBot);

                if (tick % header.SnapshotEveryTicks != 0)
                {
                    continue;
                }

                int commitSnapshotTick = ScenarioRunner.WaitForExpectedSnapshotTickForTests(clients, pendingSnapshotsByBot, tick);

                foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                {
                    SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[client.BotIndex];
                    if (!pending.TryGetValue(commitSnapshotTick, out Snapshot? snapshot))
                    {
                        throw new InvalidOperationException($"Missing synchronized snapshot for bot={client.BotIndex} at replay tick={tick}.");
                    }

                    committedSnapshots[(client.BotIndex, tick)] = snapshot;

                    foreach (int oldTick in pending.Keys.Where(t => t <= commitSnapshotTick).ToList())
                    {
                        pending.Remove(oldTick);
                    }
                }
            }

            if (reader.TryReadNext(out ReplayEvent finalEvt))
            {
                if (finalEvt.RecordType != ReplayRecordType.FinalChecksum)
                {
                    throw new InvalidDataException($"Unexpected replay record after ticks: {finalEvt.RecordType}.");
                }

                expectedChecksum = finalEvt.FinalChecksumHex;

                if (reader.TryReadNext(out _))
                {
                    throw new InvalidDataException("Replay contains trailing records after final checksum.");
                }
            }

            foreach (((int botIndex, int tick), Snapshot snapshot) in committedSnapshots
                         .OrderBy(entry => entry.Key.Tick)
                         .ThenBy(entry => entry.Key.BotIndex)
                         .Select(entry => (entry.Key, entry.Value)))
            {
                checksum.AppendSnapshot(tick, botIndex, snapshot);
            }

            string finalChecksum = checksum.BuildHexLower();

            if (enforceExpectedChecksum &&
                !string.IsNullOrWhiteSpace(expectedChecksum) &&
                !string.Equals(expectedChecksum, finalChecksum, StringComparison.Ordinal))
            {
                IReadOnlyList<TickReport> expectedTickReports = actualTickReports.ToArray();
                int divergentTick = FindFirstDivergentTick(expectedTickReports, actualTickReports) ?? header.TickCount;
                ReplayMismatchSummary summary = BuildSummary(expectedTickReports, actualTickReports, divergentTick, expectedChecksum, finalChecksum);
                string artifactsDirectory = WriteMismatchArtifacts(header, verifyOptions, expectedChecksum, finalChecksum, expectedTickReports, actualTickReports, summary);
                string message =
                    $"Replay checksum mismatch. expected_checksum={expectedChecksum} actual_checksum={finalChecksum} {summary.ToSingleLine()} artifact_output_dir={artifactsDirectory}";
                logger.LogError(BotRunnerLogEvents.ScenarioEnd, "{Message}", message);
                throw new ReplayVerificationException(message, divergentTick, expectedChecksum, finalChecksum, artifactsDirectory);
            }

            logger.LogInformation(BotRunnerLogEvents.ScenarioEnd, "ScenarioEnd checksum={Checksum} ticks={Ticks}", finalChecksum, header.TickCount);
            return new ReplayExecutionResult(finalChecksum, expectedChecksum);
        }
        finally
        {
            foreach (BotClient client in clients)
            {
                client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static string WriteMismatchArtifacts(
        ReplayHeader header,
        ReplayVerifyOptions? verifyOptions,
        string expectedChecksum,
        string actualChecksum,
        IReadOnlyList<TickReport> expectedTickReports,
        IReadOnlyList<TickReport> actualTickReports,
        ReplayMismatchSummary summary)
    {
        string outputDir = verifyOptions?.OutputDir
            ?? Path.Combine("artifacts", "replay-mismatch", $"seed-{header.ServerSeed}_bots-{header.BotCount}_ticks-{header.TickCount}");
        Directory.CreateDirectory(outputDir);

        File.WriteAllText(Path.Combine(outputDir, "expected_checksum.txt"), expectedChecksum + Environment.NewLine);
        File.WriteAllText(Path.Combine(outputDir, "actual_checksum.txt"), actualChecksum + Environment.NewLine);
        WriteJsonl(Path.Combine(outputDir, "tickreport_expected.jsonl"), expectedTickReports);
        WriteJsonl(Path.Combine(outputDir, "tickreport_actual.jsonl"), actualTickReports);
        File.WriteAllText(Path.Combine(outputDir, "mismatch_summary.txt"), summary.ToText());

        return outputDir;
    }

    private static void WriteJsonl(string filePath, IReadOnlyList<TickReport> reports)
    {
        using StreamWriter writer = new(filePath, append: false);
        foreach (TickReport report in reports.OrderBy(r => r.Tick))
        {
            writer.WriteLine(JsonSerializer.Serialize(report));
        }
    }

    internal static int? FindFirstDivergentTick(IReadOnlyList<TickReport> expectedReports, IReadOnlyList<TickReport> actualReports)
    {
        int minCount = Math.Min(expectedReports.Count, actualReports.Count);
        for (int i = 0; i < minCount; i++)
        {
            if (expectedReports[i].Tick != actualReports[i].Tick)
            {
                return Math.Min(expectedReports[i].Tick, actualReports[i].Tick);
            }

            if (!string.Equals(expectedReports[i].WorldChecksum, actualReports[i].WorldChecksum, StringComparison.Ordinal) ||
                !Equals(expectedReports[i], actualReports[i]))
            {
                return expectedReports[i].Tick;
            }
        }

        if (expectedReports.Count == actualReports.Count)
        {
            return null;
        }

        return expectedReports.Count > actualReports.Count
            ? expectedReports[minCount].Tick
            : actualReports[minCount].Tick;
    }

    internal static ReplayMismatchSummary BuildSummary(
        IReadOnlyList<TickReport> expectedReports,
        IReadOnlyList<TickReport> actualReports,
        int firstTick,
        string expectedFinalChecksum,
        string actualFinalChecksum,
        int topN = 10)
    {
        TickReport? expectedTickReport = expectedReports.FirstOrDefault(r => r.Tick == firstTick);
        TickReport? actualTickReport = actualReports.FirstOrDefault(r => r.Tick == firstTick);

        string expectedChecksumAtTick = expectedTickReport?.WorldChecksum ?? expectedFinalChecksum;
        string actualChecksumAtTick = actualTickReport?.WorldChecksum ?? actualFinalChecksum;

        return new ReplayMismatchSummary(
            FirstDivergentTick: firstTick,
            ExpectedFinalChecksum: expectedFinalChecksum,
            ActualFinalChecksum: actualFinalChecksum,
            ExpectedChecksumAtTick: expectedChecksumAtTick,
            ActualChecksumAtTick: actualChecksumAtTick,
            EntityCountByTypeDiffs: BuildCountIntDiffs(expectedTickReport?.EntityCountByType ?? [], actualTickReport?.EntityCountByType ?? [], topN),
            InventoryTotalsDiffs: BuildCountIntDiffs(expectedTickReport?.InventoryTotals ?? [], actualTickReport?.InventoryTotals ?? [], topN),
            LootCountDiff: (actualTickReport?.LootCount ?? 0) - (expectedTickReport?.LootCount ?? 0),
            WalletTotalsDiffs: BuildCountLongDiffs(expectedTickReport?.WalletTotals ?? [], actualTickReport?.WalletTotals ?? [], topN));
    }

    private static IReadOnlyList<ReplayMismatchDiffInt> BuildCountIntDiffs(
        ImmutableArray<TickReportCountInt> expected,
        ImmutableArray<TickReportCountInt> actual,
        int topN)
    {
        Dictionary<string, int> expectedMap = expected.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
        Dictionary<string, int> actualMap = actual.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
        return expectedMap.Keys
            .Concat(actualMap.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(key => new ReplayMismatchDiffInt(
                key,
                expectedMap.TryGetValue(key, out int expectedValue) ? expectedValue : 0,
                actualMap.TryGetValue(key, out int actualValue) ? actualValue : 0))
            .Where(diff => diff.Delta != 0)
            .Take(topN)
            .ToArray();
    }

    private static IReadOnlyList<ReplayMismatchDiffLong> BuildCountLongDiffs(
        ImmutableArray<TickReportCountLong> expected,
        ImmutableArray<TickReportCountLong> actual,
        int topN)
    {
        Dictionary<string, long> expectedMap = expected.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
        Dictionary<string, long> actualMap = actual.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
        return expectedMap.Keys
            .Concat(actualMap.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(key => new ReplayMismatchDiffLong(
                key,
                expectedMap.TryGetValue(key, out long expectedValue) ? expectedValue : 0,
                actualMap.TryGetValue(key, out long actualValue) ? actualValue : 0))
            .Where(diff => diff.Delta != 0)
            .Take(topN)
            .ToArray();
    }
}

public sealed record ReplayMismatchSummary(
    int FirstDivergentTick,
    string ExpectedFinalChecksum,
    string ActualFinalChecksum,
    string ExpectedChecksumAtTick,
    string ActualChecksumAtTick,
    IReadOnlyList<ReplayMismatchDiffInt> EntityCountByTypeDiffs,
    IReadOnlyList<ReplayMismatchDiffInt> InventoryTotalsDiffs,
    int LootCountDiff,
    IReadOnlyList<ReplayMismatchDiffLong> WalletTotalsDiffs)
{
    public string ToSingleLine()
    {
        return $"FirstDivergentTick={FirstDivergentTick} expected_checksum={ExpectedFinalChecksum} actual_checksum={ActualFinalChecksum} ExpectedChecksumAtTick={ExpectedChecksumAtTick} ActualChecksumAtTick={ActualChecksumAtTick}";
    }

    public string ToText()
    {
        List<string> lines =
        [
            $"FirstDivergentTick={FirstDivergentTick}",
            $"ExpectedFinalChecksum={ExpectedFinalChecksum}",
            $"ActualFinalChecksum={ActualFinalChecksum}",
            $"ExpectedChecksumAtTick={ExpectedChecksumAtTick}",
            $"ActualChecksumAtTick={ActualChecksumAtTick}",
            $"LootCountDiff={LootCountDiff}",
            "EntityCountByTypeDiffs:",
            .. EntityCountByTypeDiffs.Select(FormatDiff),
            "InventoryTotalsDiffs:",
            .. InventoryTotalsDiffs.Select(FormatDiff),
            "WalletTotalsDiffs:",
            .. WalletTotalsDiffs.Select(FormatDiff)
        ];

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string FormatDiff(ReplayMismatchDiffInt diff) =>
        $"- {diff.Key}: expected={diff.Expected} actual={diff.Actual} delta={diff.Delta}";

    private static string FormatDiff(ReplayMismatchDiffLong diff) =>
        $"- {diff.Key}: expected={diff.Expected} actual={diff.Actual} delta={diff.Delta}";
}

public sealed record ReplayMismatchDiffInt(string Key, int Expected, int Actual)
{
    public int Delta => Actual - Expected;
}

public sealed record ReplayMismatchDiffLong(string Key, long Expected, long Actual)
{
    public long Delta => Actual - Expected;
}

public sealed record ReplayExecutionResult(string Checksum, string? ExpectedChecksum);

public sealed record ReplayVerifyOptions(string? OutputDir);

public sealed class ReplayVerificationException : Exception
{
    public ReplayVerificationException(string message, int divergentTick, string expectedChecksum, string actualChecksum, string artifactsDirectory)
        : base(message)
    {
        DivergentTick = divergentTick;
        ExpectedChecksum = expectedChecksum;
        ActualChecksum = actualChecksum;
        ArtifactsDirectory = artifactsDirectory;
    }

    public int DivergentTick { get; }

    public string ExpectedChecksum { get; }

    public string ActualChecksum { get; }

    public string ArtifactsDirectory { get; }
}
