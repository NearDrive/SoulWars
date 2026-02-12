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
        List<(int RoundTick, int BotIndex, Snapshot Snapshot)> committedSnapshots = new();
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot = new();
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
            }

            _ = ConnectBots(runtime, clients, cts.Token);

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

                int commitTick = WaitForSnapshotTick(runtime, clients, pendingSnapshotsByBot, cfg.TickCount, tick, tick, cts.Token);

                foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                {
                    SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[client.BotIndex];
                    committedSnapshots.Add((tick, client.BotIndex, GetSnapshotForCommitTick(pending, commitTick)));

                    foreach (int oldTick in pending.Keys.Where(t => t <= commitTick).ToList())
                    {
                        pending.Remove(oldTick);
                    }
                }
            }

            foreach ((int roundTick, int botIndex, Snapshot snapshot) in committedSnapshots
                         .OrderBy(entry => entry.RoundTick)
                         .ThenBy(entry => entry.BotIndex))
            {
                checksum.AppendSnapshot(roundTick, botIndex, snapshot);
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
            Task connectTask = client.ConnectAndEnterAsync("127.0.0.1", runtime.BoundPort, ct);
            int maxConnectSteps = Math.Max(10_000, clients.Count * 2_000);
            int connectSteps = 0;

            while (!connectTask.IsCompleted)
            {
                if (connectSteps++ >= maxConnectSteps)
                {
                    throw new InvalidOperationException(
                        $"Bot {client.BotIndex} did not finish connect/enter handshake deterministically after {connectSteps} steps (boundPort={runtime.BoundPort}).");
                }

                ct.ThrowIfCancellationRequested();
                runtime.StepOnce();
                serverSteps++;
                Thread.Yield();
            }

            connectTask.GetAwaiter().GetResult();
        }

        return serverSteps;
    }

    private static void DrainSnapshots(
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot)
    {
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            client.DrainMessages(message =>
            {
                if (message is Snapshot snapshot)
                {
                    if (!pendingSnapshotsByBot.TryGetValue(client.BotIndex, out SortedDictionary<int, Snapshot>? pending))
                    {
                        pending = new SortedDictionary<int, Snapshot>();
                        pendingSnapshotsByBot[client.BotIndex] = pending;
                    }

                    pending[snapshot.Tick] = snapshot;
                }
            });
        }
    }

    private static int WaitForSnapshotTick(
        ServerRuntime runtime,
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot,
        int tickCount,
        int loopTick,
        int snapshotTick,
        CancellationToken ct)
    {
        int maxPasses = Math.Max(8_192, clients.Count * tickCount * 4);

        for (int passes = 0; passes < maxPasses; passes++)
        {
            ct.ThrowIfCancellationRequested();
            runtime.StepOnce();
            DrainSnapshots(clients, pendingSnapshotsByBot);

            List<int> candidateMaxTicks = new();
            bool ready = true;

            foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
            {
                SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[client.BotIndex];
                int botMax = pending.Keys.Where(t => t >= snapshotTick).DefaultIfEmpty(-1).Max();
                if (botMax < snapshotTick)
                {
                    ready = false;
                    break;
                }

                candidateMaxTicks.Add(botMax);
            }

            if (!ready)
            {
                Thread.Yield();
                continue;
            }

            int commitTick = candidateMaxTicks.Min();
            if (clients.All(client => HasSnapshotAtOrBeforeTick(pendingSnapshotsByBot[client.BotIndex], commitTick)))
            {
                return commitTick;
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
            $"Unable to synchronize snapshots (tickCount={tickCount}, loopTick={loopTick}, expectedSnapshotTick={snapshotTick}, {perBot}).");
    }

    private static Snapshot GetSnapshotForCommitTick(SortedDictionary<int, Snapshot> pending, int commitTick)
    {
        foreach ((int tick, Snapshot snapshot) in pending.Reverse())
        {
            if (tick <= commitTick)
            {
                return snapshot;
            }
        }

        throw new InvalidOperationException($"No snapshot at or before commitTick={commitTick}.");
    }

    private static bool HasSnapshotAtOrBeforeTick(SortedDictionary<int, Snapshot> pending, int commitTick)
    {
        return pending.Keys.Any(tick => tick <= commitTick);
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
