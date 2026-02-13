using Game.Protocol;
using Game.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Game.BotRunner;

public static class ReplayRunner
{
    public static string RunReplay(Stream replayStream, ILoggerFactory? loggerFactory = null)
    {
        ReplayExecutionResult result = RunReplayInternal(replayStream, loggerFactory);
        return result.Checksum;
    }

    public static ReplayExecutionResult RunReplayWithExpected(Stream replayStream, ILoggerFactory? loggerFactory = null)
    {
        return RunReplayInternal(replayStream, loggerFactory);
    }

    private static ReplayExecutionResult RunReplayInternal(Stream replayStream, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(replayStream);
        ILoggerFactory lf = loggerFactory ?? NullLoggerFactory.Instance;
        ILogger logger = lf.CreateLogger("ReplayRunner");

        using ReplayReader reader = new(replayStream);
        ReplayHeader header = reader.Header;

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
        ServerConfig serverConfig = ServerConfig.Default(header.ServerSeed) with
        {
            SnapshotEveryTicks = header.SnapshotEveryTicks
        };

        using ScenarioChecksumBuilder checksum = new();
        List<BotClient> clients = new(header.BotCount);
        Dictionary<(int BotIndex, int Tick), Snapshot> committedSnapshots = new();
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot = new();
        ServerHost host = new(serverConfig, lf);

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
                    BotClient client = clients[botIndex];
                    client.SendInput(tick, move.MoveX, move.MoveY);
                    if (move.AttackTargetId is int targetId && client.EntityId is int attackerId)
                    {
                        client.SendAttackIntent(tick, attackerId, targetId);
                    }
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
}

public sealed record ReplayExecutionResult(string Checksum, string? ExpectedChecksum);
