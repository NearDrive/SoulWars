using Game.Protocol;
using Game.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
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
                string artifactsDirectory = WriteMismatchArtifacts(header, verifyOptions, expectedChecksum, finalChecksum, expectedTickReports, actualTickReports);
                string message =
                    $"Replay checksum mismatch at tick={divergentTick}. expected={expectedChecksum} actual={finalChecksum} artifacts={artifactsDirectory}";
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
        IReadOnlyList<TickReport> actualTickReports)
    {
        string outputDir = verifyOptions?.OutputDir
            ?? Path.Combine("artifacts", "replay-mismatch", $"seed-{header.ServerSeed}_bots-{header.BotCount}_ticks-{header.TickCount}");
        Directory.CreateDirectory(outputDir);

        File.WriteAllText(Path.Combine(outputDir, "expected_checksum.txt"), expectedChecksum + Environment.NewLine);
        File.WriteAllText(Path.Combine(outputDir, "actual_checksum.txt"), actualChecksum + Environment.NewLine);
        WriteJsonl(Path.Combine(outputDir, "tickreport_expected.jsonl"), expectedTickReports);
        WriteJsonl(Path.Combine(outputDir, "tickreport_actual.jsonl"), actualTickReports);

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
}

public sealed record ReplayExecutionResult(string Checksum, string? ExpectedChecksum);

public sealed record ReplayVerifyOptions(string? OutputDir);

public sealed class ReplayVerificationException : InvalidDataException
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
