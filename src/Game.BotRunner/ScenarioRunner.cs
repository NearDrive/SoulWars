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
        List<ReceivedSnapshot> snapshots = new();
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
                    if (msg is Snapshot snapshot)
                    {
                        snapshots.Add(new ReceivedSnapshot(index, snapshot));
                    }
                });
            }

            foreach (ReceivedSnapshot received in snapshots
                         .OrderBy(s => s.Snapshot.Tick)
                         .ThenBy(s => s.BotIndex)
                         .ThenBy(s => s.Snapshot.ZoneId))
            {
                checksum.AppendSnapshot(received.BotIndex, received.Snapshot);
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
        List<Task> tasks = clients
            .OrderBy(client => client.BotIndex)
            .Select(client => client.ConnectAndEnterAsync("127.0.0.1", runtime.BoundPort, ct))
            .ToList();

        while (!tasks.All(task => task.IsCompleted))
        {
            runtime.StepOnce();
            DrainAll(clients, (_, _) => { });
        }

        Task.WhenAll(tasks).GetAwaiter().GetResult();
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

    private readonly record struct ReceivedSnapshot(int BotIndex, Snapshot Snapshot);
}
