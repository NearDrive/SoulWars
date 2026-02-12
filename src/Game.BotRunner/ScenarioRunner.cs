using System.Net;
using Game.Protocol;
using Game.Server;

namespace Game.BotRunner;

public sealed class ScenarioRunner
{
    public static string Run(ScenarioConfig cfg) => RunDetailed(cfg).Checksum;

    public static ScenarioResult RunDetailed(ScenarioConfig cfg)
    {
        Validate(cfg);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
        ServerConfig serverConfig = ServerConfig.Default(cfg.ServerSeed) with
        {
            SnapshotEveryTicks = cfg.SnapshotEveryTicks
        };

        using ScenarioChecksumBuilder checksum = new();
        List<BotClient> clients = new(cfg.BotCount);
        Dictionary<(int BotIndex, int Tick), Snapshot> committedSnapshots = new();
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot = new();
        Dictionary<int, int> lastCommittedSnapshotTickByBot = new();
        ServerRuntime runtime = new();

        try
        {
            runtime.StartAsync(serverConfig, IPAddress.Loopback, 0, cts.Token).GetAwaiter().GetResult();

            List<BotAgent> agents = new(cfg.BotCount);
            for (int i = 0; i < cfg.BotCount; i++)
            {
                BotConfig botConfig = new(
                    BotIndex: i,
                    InputSeed: cfg.BaseBotSeed + (i * 101),
                    ZoneId: cfg.ZoneId);

                BotClient client = new(i, botConfig.ZoneId);
                clients.Add(client);
                agents.Add(new BotAgent(botConfig));
                pendingSnapshotsByBot[i] = new SortedDictionary<int, Snapshot>();
                lastCommittedSnapshotTickByBot[i] = 0;
            }

            _ = ConnectBots(runtime, clients, cts.Token);

            // Ignore snapshots that may have been received during handshake.
            DrainAllMessages(clients);
            foreach (SortedDictionary<int, Snapshot> pending in pendingSnapshotsByBot.Values)
            {
                pending.Clear();
            }

            for (int tick = 1; tick <= cfg.TickCount; tick++)
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    (sbyte mx, sbyte my) = agents[i].GetMoveForTick(tick);
                    clients[i].SendInput(tick, mx, my);
                }

                runtime.StepOnce();
                DrainSnapshots(clients, pendingSnapshotsByBot);

                if (tick % cfg.SnapshotEveryTicks != 0)
                {
                    continue;
                }

                int commitSnapshotTick = WaitForNextRoundSnapshots(runtime, clients, pendingSnapshotsByBot, lastCommittedSnapshotTickByBot, cfg.TickCount, tick, cts.Token);

                foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                {
                    SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[client.BotIndex];
                    if (!pending.TryGetValue(commitSnapshotTick, out Snapshot? snapshot))
                    {
                        throw new InvalidOperationException(
                            $"Missing synchronized snapshot for bot={client.BotIndex} scenarioTick={tick} commitSnapshotTick={commitSnapshotTick}.");
                    }

                    int selectedTick = commitSnapshotTick;
                    committedSnapshots[(client.BotIndex, tick)] = snapshot;
                    lastCommittedSnapshotTickByBot[client.BotIndex] = selectedTick;

                    foreach (int oldTick in pending.Keys.Where(t => t <= selectedTick).ToList())
                    {
                        pending.Remove(oldTick);
                    }
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
            BotScenarioStats[] stats = clients
                .OrderBy(client => client.BotIndex)
                .Select(client => new BotScenarioStats(
                    BotIndex: client.BotIndex,
                    SessionId: client.SessionId?.Value,
                    EntityId: client.EntityId,
                    SnapshotsReceived: client.SnapshotsReceived))
                .ToArray();

            return new ScenarioResult(finalChecksum, stats);
        }
        finally
        {
            foreach (BotClient client in clients)
            {
                client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static int ConnectBots(ServerRuntime runtime, IReadOnlyList<BotClient> clients, CancellationToken ct)
    {
        int serverSteps = 0;

        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            client.ConnectAsync("127.0.0.1", runtime.BoundPort, ct).GetAwaiter().GetResult();

            int maxConnectSteps = Math.Max(50_000, clients.Count * 10_000);
            int connectSteps = 0;

            while (!client.HandshakeDone)
            {
                if (connectSteps++ >= maxConnectSteps)
                {
                    throw new InvalidOperationException(
                        $"Bot {client.BotIndex} handshake failed (boundPort={runtime.BoundPort}, steps={connectSteps}, hasWelcome={client.HasWelcome}, hasEntered={client.HasEntered}, sessionId={client.SessionId?.Value.ToString() ?? "null"}, entityId={client.EntityId?.ToString() ?? "null"}, lastSnapshotTick={client.LastSnapshotTick}, snapshotsReceived={client.SnapshotsReceived}).");
                }

                ct.ThrowIfCancellationRequested();
                runtime.StepOnce();
                serverSteps++;
                DrainAllMessages(clients);

                if (client.HasWelcome)
                {
                    client.EnterZone();
                }
            }
        }

        return serverSteps;
    }

    private static void DrainAllMessages(IReadOnlyList<BotClient> clients)
    {
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            client.PumpMessages(_ => { });
        }
    }

    private static void DrainSnapshots(
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot)
    {
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            client.PumpMessages(message =>
            {
                if (message is Snapshot snapshot)
                {
                    pendingSnapshotsByBot[client.BotIndex][snapshot.Tick] = snapshot;
                }
            });
        }
    }

    private static int WaitForNextRoundSnapshots(
        ServerRuntime runtime,
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot,
        IReadOnlyDictionary<int, int> lastCommittedSnapshotTickByBot,
        int tickCount,
        int loopTick,
        CancellationToken ct)
    {
        int maxPolls = Math.Max(50_000, clients.Count * tickCount * 10);

        // IMPORTANT: the simulation tick is already advanced by the main loop StepOnce().
        // During synchronization we only poll/drain client buffers to avoid introducing
        // timing-dependent extra simulation steps.
        for (int poll = 0; poll < maxPolls; poll++)
        {
            ct.ThrowIfCancellationRequested();
            DrainSnapshots(clients, pendingSnapshotsByBot);

            bool ready = clients
                .OrderBy(c => c.BotIndex)
                .All(client => HasSnapshotAfterTick(pendingSnapshotsByBot[client.BotIndex], lastCommittedSnapshotTickByBot[client.BotIndex]));

            if (ready)
            {
                return FindCommonSnapshotTick(clients, pendingSnapshotsByBot, lastCommittedSnapshotTickByBot);
            }

            Thread.Yield();
        }

        string perBot = string.Join(", ",
            clients
                .OrderBy(c => c.BotIndex)
                .Select(c =>
                {
                    SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[c.BotIndex];
                    int max = pending.Count == 0 ? 0 : pending.Keys.Max();
                    return $"bot{c.BotIndex}:maxPending={max}";
                }));

        throw new InvalidOperationException(
            $"Unable to synchronize snapshots (tickCount={tickCount}, loopTick={loopTick}, {perBot}).");
    }

    private static bool HasSnapshotAfterTick(SortedDictionary<int, Snapshot> pending, int tick)
    {
        return pending.Keys.Any(t => t > tick);
    }

    private static int FindCommonSnapshotTick(
        IReadOnlyList<BotClient> clients,
        IReadOnlyDictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot,
        IReadOnlyDictionary<int, int> lastCommittedSnapshotTickByBot)
    {
        IEnumerable<int>? common = null;

        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            int lastCommitted = lastCommittedSnapshotTickByBot[client.BotIndex];
            IEnumerable<int> ticks = pendingSnapshotsByBot[client.BotIndex].Keys.Where(t => t > lastCommitted);
            common = common is null ? ticks : common.Intersect(ticks);
        }

        if (common is null)
        {
            throw new InvalidOperationException("Cannot resolve common snapshot tick for zero clients.");
        }

        int commitTick = common.DefaultIfEmpty(0).Min();
        if (commitTick <= 0)
        {
            throw new InvalidOperationException("Unable to find common synchronized snapshot tick.");
        }

        return commitTick;
    }

    private static void Validate(ScenarioConfig cfg)
    {
        if (cfg.TickCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cfg), "TickCount must be > 0.");
        }

        if (cfg.SnapshotEveryTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cfg), "SnapshotEveryTicks must be > 0.");
        }

        if (cfg.BotCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cfg), "BotCount must be > 0.");
        }
    }
}
