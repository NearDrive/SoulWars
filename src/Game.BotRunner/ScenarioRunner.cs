using Game.Protocol;
using Game.Server;

namespace Game.BotRunner;

public sealed class ScenarioRunner
{
    public static string Run(ScenarioConfig cfg) => RunDetailed(cfg).Checksum;

    public static ScenarioResult RunDetailed(ScenarioConfig cfg) => RunDetailedInternal(cfg, replayWriter: null);

    public static ScenarioResult RunAndRecord(ScenarioConfig cfg, Stream replayOut)
    {
        ArgumentNullException.ThrowIfNull(replayOut);
        ReplayHeader header = ReplayHeader.FromScenarioConfig(cfg);
        using ReplayWriter writer = new(replayOut, header);
        return RunDetailedInternal(cfg, writer);
    }

    private static ScenarioResult RunDetailedInternal(ScenarioConfig cfg, ReplayWriter? replayWriter)
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
        ServerHost host = new(serverConfig);

        try
        {
            List<BotAgent> agents = new(cfg.BotCount);
            for (int i = 0; i < cfg.BotCount; i++)
            {
                BotConfig botConfig = new(
                    BotIndex: i,
                    InputSeed: cfg.BaseBotSeed + (i * 101),
                    ZoneId: cfg.ZoneId);

                InMemoryEndpoint endpoint = new();
                host.Connect(endpoint);

                BotClient client = new(i, botConfig.ZoneId, endpoint);
                clients.Add(client);
                agents.Add(new BotAgent(botConfig));
                pendingSnapshotsByBot[i] = new SortedDictionary<int, Snapshot>();
            }

            ConnectBotsForTests(host, clients, cts.Token);

            // Ignore snapshots that may have been received during handshake.
            DrainAllMessagesForTests(clients);
            foreach (SortedDictionary<int, Snapshot> pending in pendingSnapshotsByBot.Values)
            {
                pending.Clear();
            }

            ReplayMove[] tickMoves = new ReplayMove[cfg.BotCount];

            for (int tick = 1; tick <= cfg.TickCount; tick++)
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    (sbyte mx, sbyte my) = agents[i].GetMoveForTick(tick);
                    tickMoves[i] = new ReplayMove(mx, my);
                    clients[i].SendInput(tick, mx, my);
                }

                replayWriter?.WriteTickInputs(tick, tickMoves);

                host.StepOnce();
                DrainSnapshotsForTests(clients, pendingSnapshotsByBot);

                if (tick % cfg.SnapshotEveryTicks != 0)
                {
                    continue;
                }

                int commitSnapshotTick = WaitForExpectedSnapshotTickForTests(clients, pendingSnapshotsByBot, tick);

                foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                {
                    SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[client.BotIndex];
                    if (!pending.TryGetValue(commitSnapshotTick, out Snapshot? snapshot))
                    {
                        throw new InvalidOperationException(
                            $"Missing synchronized snapshot for bot={client.BotIndex} scenarioTick={tick} commitSnapshotTick={commitSnapshotTick}.");
                    }

                    committedSnapshots[(client.BotIndex, tick)] = snapshot;

                    foreach (int oldTick in pending.Keys.Where(t => t <= commitSnapshotTick).ToList())
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
            replayWriter?.WriteFinalChecksum(finalChecksum);

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
        }
    }

    internal static void ConnectBotsForTests(ServerHost host, IReadOnlyList<BotClient> clients, CancellationToken ct)
    {
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            int maxConnectSteps = Math.Max(10_000, clients.Count * 1_000);
            int connectSteps = 0;

            while (!client.HandshakeDone)
            {
                if (connectSteps++ >= maxConnectSteps)
                {
                    throw new InvalidOperationException(
                        $"Bot {client.BotIndex} handshake failed (steps={connectSteps}, hasWelcome={client.HasWelcome}, hasEntered={client.HasEntered}, sessionId={client.SessionId?.Value.ToString() ?? "null"}, entityId={client.EntityId?.ToString() ?? "null"}, lastSnapshotTick={client.LastSnapshotTick}, snapshotsReceived={client.SnapshotsReceived}).");
                }

                ct.ThrowIfCancellationRequested();
                DrainAllMessagesForTests(clients);

                if (client.HasWelcome)
                {
                    client.EnterZone();
                }

                host.ProcessInboundOnce();
                DrainAllMessagesForTests(clients);
            }
        }
    }

    internal static void DrainAllMessagesForTests(IReadOnlyList<BotClient> clients)
    {
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            client.PumpMessages(_ => { });
        }
    }

    internal static void DrainSnapshotsForTests(
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

    internal static int WaitForExpectedSnapshotTickForTests(
        IReadOnlyList<BotClient> clients,
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot,
        int expectedSnapshotTick)
    {
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[client.BotIndex];
            foreach (int staleTick in pending.Keys.Where(t => t < expectedSnapshotTick).ToList())
            {
                pending.Remove(staleTick);
            }
        }

        bool ready = clients
            .OrderBy(c => c.BotIndex)
            .All(client => pendingSnapshotsByBot[client.BotIndex].ContainsKey(expectedSnapshotTick));

        if (ready)
        {
            return expectedSnapshotTick;
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
            $"Unable to synchronize snapshots (expectedSnapshotTick={expectedSnapshotTick}, {perBot}).");
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
