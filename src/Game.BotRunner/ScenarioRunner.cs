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

                int maxSyncSteps = Math.Max(256, cfg.BotCount * 32);
                int syncSteps = 0;

                while (true)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    if (clients.All(client => client.LastSnapshotTick > lastCommittedSnapshotTick))
                    {
                        int commonTick = clients.Min(client => client.LastSnapshotTick);
                        if (commonTick > lastCommittedSnapshotTick && AllBotsHavePendingTick(clients, pendingSnapshotsByBot, commonTick))
                        {
                            foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                            {
                                SortedDictionary<int, Snapshot> pendingByTick = pendingSnapshotsByBot[client.BotIndex];
                                snapshots[(client.BotIndex, commonTick)] = pendingByTick[commonTick];

                                List<int> consumedTicks = pendingByTick.Keys.Where(key => key <= commonTick).ToList();
                                foreach (int consumedTick in consumedTicks)
                                {
                                    pendingByTick.Remove(consumedTick);
                                }
                            }

                            lastCommittedSnapshotTick = commonTick;
                            break;
                        }
                    }

                    if (syncSteps++ >= maxSyncSteps)
                    {
                        throw new InvalidOperationException($"Unable to synchronize snapshots after tick {lastCommittedSnapshotTick} (loopTick={tick}).");
                    }

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
                DrainAll(clients, (_, _) => { }, skipBotIndex: client.BotIndex);
            }

            connectTask.GetAwaiter().GetResult();
        }
    }

    private static void DrainAll(IReadOnlyList<BotClient> clients, Action<int, IServerMessage> onMessage, int? skipBotIndex = null)
    {
        bool drainedAny;
        do
        {
            drainedAny = false;
            foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
            {
                if (skipBotIndex == client.BotIndex)
                {
                    continue;
                }

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

    private static bool AllBotsHavePendingTick(
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot,
        int tick)
    {
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            if (!pendingSnapshotsByBot.TryGetValue(client.BotIndex, out SortedDictionary<int, Snapshot>? pendingByTick))
            {
                return false;
            }

            if (!pendingByTick.ContainsKey(tick))
            {
                return false;
            }
        }

        return true;
    }
}
