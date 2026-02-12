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
        Dictionary<(int BotIndex, int SnapshotTick), Snapshot> committedSnapshots = new();
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot = new();
        Dictionary<int, int> lastRoundSnapshotTickByBot = new();
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
                lastRoundSnapshotTickByBot[i] = 0;
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
                DrainSnapshots(clients, pendingSnapshotsByBot);

                if (tick % cfg.SnapshotEveryTicks != 0)
                {
                    continue;
                }

                int maxSyncSteps = Math.Max(512, cfg.BotCount * 64);
                int syncSteps = 0;
                int roundTick = 0;
                Dictionary<int, Snapshot>? roundSnapshots = null;

                while (!TryBuildRound(
                           clients,
                           pendingSnapshotsByBot,
                           lastRoundSnapshotTickByBot,
                           out roundTick,
                           out roundSnapshots))
                {
                    if (syncSteps++ >= maxSyncSteps)
                    {
                        throw new InvalidOperationException($"Unable to synchronize snapshots after loopTick={tick}.");
                    }

                    cts.Token.ThrowIfCancellationRequested();
                    runtime.StepOnce();
                    DrainSnapshots(clients, pendingSnapshotsByBot);
                }

                if (roundSnapshots is null)
                {
                    throw new InvalidOperationException("Round synchronization produced null snapshots.");
                }

                foreach ((int botIndex, Snapshot snapshot) in roundSnapshots.OrderBy(kvp => kvp.Key))
                {
                    committedSnapshots[(botIndex, roundTick)] = snapshot;
                    lastRoundSnapshotTickByBot[botIndex] = roundTick;

                    SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[botIndex];
                    foreach (int oldTick in pending.Keys.Where(t => t <= roundTick).ToList())
                    {
                        pending.Remove(oldTick);
                    }
                }
            }

            foreach (((int botIndex, int snapshotTick), Snapshot snapshot) in committedSnapshots
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
            }

            connectTask.GetAwaiter().GetResult();
        }
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
                    pendingSnapshotsByBot[client.BotIndex][snapshot.Tick] = snapshot;
                }
            });
        }
    }

    private static bool TryBuildRound(
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot,
        Dictionary<int, int> lastRoundSnapshotTickByBot,
        out int roundTick,
        out Dictionary<int, Snapshot>? roundSnapshots)
    {
        roundTick = 0;
        roundSnapshots = null;

        Dictionary<int, int> firstNewTickByBot = new();
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            int botIndex = client.BotIndex;
            SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[botIndex];
            int lastRoundTick = lastRoundSnapshotTickByBot[botIndex];

            int nextTick = pending.Keys.FirstOrDefault(t => t > lastRoundTick);
            if (nextTick <= lastRoundTick)
            {
                return false;
            }

            firstNewTickByBot[botIndex] = nextTick;
        }

        roundTick = firstNewTickByBot.Values.Min();

        Dictionary<int, Snapshot> byBot = new();
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            int botIndex = client.BotIndex;
            SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[botIndex];
            if (!pending.TryGetValue(roundTick, out Snapshot? snapshot))
            {
                return false;
            }

            byBot[botIndex] = snapshot;
        }

        roundSnapshots = byBot;
        return true;
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
