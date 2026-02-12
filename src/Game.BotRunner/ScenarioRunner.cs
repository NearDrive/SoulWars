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
            }

            DrainSnapshotsUntilQuiet(clients, pendingSnapshotsByBot, cts.Token);

            foreach (int commonTick in GetCommonSnapshotTicks(clients, pendingSnapshotsByBot))
            {
                foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                {
                    committedSnapshots[(client.BotIndex, commonTick)] = pendingSnapshotsByBot[client.BotIndex][commonTick];
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

    private static void DrainSnapshotsUntilQuiet(
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot,
        CancellationToken ct)
    {
        int quietPasses = 0;
        int maxPasses = 256;

        while (quietPasses < 3 && maxPasses-- > 0)
        {
            ct.ThrowIfCancellationRequested();

            int before = pendingSnapshotsByBot.Sum(kvp => kvp.Value.Count);
            DrainSnapshots(clients, pendingSnapshotsByBot);
            int after = pendingSnapshotsByBot.Sum(kvp => kvp.Value.Count);

            if (after == before)
            {
                quietPasses++;
            }
            else
            {
                quietPasses = 0;
            }
        }
    }

    private static IEnumerable<int> GetCommonSnapshotTicks(
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot)
    {
        List<HashSet<int>> sets = new();
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            sets.Add(pendingSnapshotsByBot[client.BotIndex].Keys.ToHashSet());
        }

        if (sets.Count == 0)
        {
            return Enumerable.Empty<int>();
        }

        HashSet<int> common = new(sets[0]);
        for (int i = 1; i < sets.Count; i++)
        {
            common.IntersectWith(sets[i]);
            if (common.Count == 0)
            {
                break;
            }
        }

        return common.OrderBy(t => t).ToArray();
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
