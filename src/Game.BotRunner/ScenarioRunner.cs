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

            int serverStepCount = ConnectBots(runtime, clients, cts.Token);

            for (int tick = 1; tick <= cfg.TickCount; tick++)
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    (sbyte mx, sbyte my) = agents[i].GetMoveForTick(tick);
                    clients[i].SendInput(tick, mx, my);
                }

                runtime.StepOnce();
                serverStepCount++;
                DrainSnapshots(clients, pendingSnapshotsByBot);

                if (serverStepCount % cfg.SnapshotEveryTicks != 0)
                {
                    continue;
                }

                WaitForSnapshotTick(clients, pendingSnapshotsByBot, serverStepCount, cts.Token);

                foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                {
                    SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[client.BotIndex];
                    committedSnapshots[(client.BotIndex, serverStepCount)] = pending[serverStepCount];

                    foreach (int oldTick in pending.Keys.Where(t => t <= serverStepCount).ToList())
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
                    pendingSnapshotsByBot[client.BotIndex][snapshot.Tick] = snapshot;
                }
            });
        }
    }

    private static void WaitForSnapshotTick(
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot,
        int snapshotTick,
        CancellationToken ct)
    {
        int maxPasses = Math.Max(512, clients.Count * 64);
        int passes = 0;

        while (!clients.All(client => pendingSnapshotsByBot[client.BotIndex].ContainsKey(snapshotTick)))
        {
            if (passes++ >= maxPasses)
            {
                throw new InvalidOperationException($"Unable to synchronize snapshots for tick {snapshotTick}.");
            }

            ct.ThrowIfCancellationRequested();
            DrainSnapshots(clients, pendingSnapshotsByBot);
            Thread.Yield();
        }
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
