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
        Dictionary<(int BotIndex, int SnapshotTick), Snapshot> snapshots = new();
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot = new();
        int lastCommittedSnapshotTick = 0;
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
            }

            ConnectBots(runtime, clients, cts.Token);

            for (int tick = 1; tick <= cfg.TickCount; tick++)
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    (sbyte mx, sbyte my) = agents[i].GetMoveForTick(tick);
                    clients[i].SendInput(tick, mx, my);
                }

                runtime.StepOnce();

                DrainAll(clients, (index, msg) =>
                {
                    if (msg is not Snapshot snapshot)
                    {
                        return;
                    }

                    if (!pendingSnapshotsByBot.TryGetValue(index, out SortedDictionary<int, Snapshot>? pendingByTick))
                    {
                        pendingByTick = new SortedDictionary<int, Snapshot>();
                        pendingSnapshotsByBot[index] = pendingByTick;
                    }

                    pendingByTick[snapshot.Tick] = snapshot;
                });

                if (tick % cfg.SnapshotEveryTicks != 0)
                {
                    continue;
                }

                int maxDrainAttempts = Math.Max(32, cfg.BotCount * 8);
                int attempts = 0;
                int synchronizedTick;

                while (!TryTakeSynchronizedSnapshotSetAfter(
                           clients,
                           pendingSnapshotsByBot,
                           lastCommittedSnapshotTick,
                           out synchronizedTick))
                {
                    if (attempts++ >= maxDrainAttempts)
                    {
                        throw new InvalidOperationException($"Unable to synchronize snapshots after tick {lastCommittedSnapshotTick} (loopTick={tick}).");
                    }

                    cts.Token.ThrowIfCancellationRequested();

                    DrainAll(clients, (index, msg) =>
                    {
                        if (msg is not Snapshot snapshot)
                        {
                            return;
                        }

                        if (!pendingSnapshotsByBot.TryGetValue(index, out SortedDictionary<int, Snapshot>? pendingByTick))
                        {
                            pendingByTick = new SortedDictionary<int, Snapshot>();
                            pendingSnapshotsByBot[index] = pendingByTick;
                        }

                        pendingByTick[snapshot.Tick] = snapshot;
                    });
                }

                foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                {
                    SortedDictionary<int, Snapshot> pendingByTick = pendingSnapshotsByBot[client.BotIndex];
                    snapshots[(client.BotIndex, synchronizedTick)] = pendingByTick[synchronizedTick];
                    pendingByTick.Remove(synchronizedTick);
                }

                lastCommittedSnapshotTick = synchronizedTick;

                while (TryTakeSynchronizedSnapshotSetAfter(
                           clients,
                           pendingSnapshotsByBot,
                           lastCommittedSnapshotTick,
                           out int additionalSynchronizedTick))
                {
                    foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                    {
                        SortedDictionary<int, Snapshot> pendingByTick = pendingSnapshotsByBot[client.BotIndex];
                        snapshots[(client.BotIndex, additionalSynchronizedTick)] = pendingByTick[additionalSynchronizedTick];
                        pendingByTick.Remove(additionalSynchronizedTick);
                    }

                    lastCommittedSnapshotTick = additionalSynchronizedTick;
                }
            }

            foreach (((int botIndex, int snapshotTick), Snapshot snapshot) in snapshots
                         .OrderBy(kvp => kvp.Key.SnapshotTick)
                         .ThenBy(kvp => kvp.Key.BotIndex)
                         .Select(kvp => (kvp.Key, kvp.Value)))
            {
                checksum.AppendSnapshot(snapshotTick, botIndex, snapshot);
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

    private static void ConnectBots(ServerRuntime runtime, IReadOnlyList<BotClient> clients, CancellationToken ct)
    {
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            Task connectTask = client.ConnectAndEnterAsync("127.0.0.1", runtime.BoundPort, ct);
            int maxConnectSteps = Math.Max(64, clients.Count * 32);
            int connectSteps = 0;

            while (!connectTask.IsCompleted)
            {
                if (connectSteps++ >= maxConnectSteps)
                {
                    throw new InvalidOperationException($"Bot {client.BotIndex} did not finish connect/enter handshake deterministically.");
                }

                ct.ThrowIfCancellationRequested();
                runtime.StepOnce();
                DrainAll(clients, (_, _) => { });
            }

            connectTask.GetAwaiter().GetResult();
        }
    }

    private static void DrainAll(IReadOnlyList<BotClient> clients, Action<int, IServerMessage> onMessage)
    {
        bool drainedAny;
        do
        {
            drainedAny = false;
            foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
            {
                client.DrainMessages(message =>
                {
                    drainedAny = true;
                    onMessage(client.BotIndex, message);
                });
            }
        }
        while (drainedAny);
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

    private static bool TryTakeSynchronizedSnapshotSetAfter(
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot,
        int afterTick,
        out int snapshotTick)
    {
        snapshotTick = 0;

        if (clients.Count == 0)
        {
            return false;
        }

        List<int>[] tickLists = clients
            .OrderBy(client => client.BotIndex)
            .Select(client =>
            {
                if (!pendingSnapshotsByBot.TryGetValue(client.BotIndex, out SortedDictionary<int, Snapshot>? pending))
                {
                    return new List<int>();
                }

                return pending.Keys.Where(key => key > afterTick).ToList();
            })
            .ToArray();

        if (tickLists.Any(list => list.Count == 0))
        {
            return false;
        }

        HashSet<int> common = new(tickLists[0]);
        for (int i = 1; i < tickLists.Length; i++)
        {
            common.IntersectWith(tickLists[i]);
            if (common.Count == 0)
            {
                return false;
            }
        }

        snapshotTick = common.Min();
        return true;
    }
}
