using Game.Core;
using Game.Protocol;
using Game.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Game.BotRunner;

public sealed class ScenarioRunner
{
    private readonly ILogger<ScenarioRunner> _logger;

    public ScenarioRunner(ILoggerFactory? loggerFactory = null)
    {
        LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = LoggerFactory.CreateLogger<ScenarioRunner>();
    }

    public ILoggerFactory LoggerFactory { get; }

    public static string Run(ScenarioConfig cfg) => new ScenarioRunner().RunDetailed(cfg).Checksum;

    public static ScenarioResult RunDetailed(ScenarioConfig cfg, ILoggerFactory? loggerFactory = null) => new ScenarioRunner(loggerFactory).RunDetailed(cfg);

    public static ScenarioResult RunAndRecord(ScenarioConfig cfg, Stream replayOut) => new ScenarioRunner().RunAndRecordDetailed(cfg, replayOut);

    public ScenarioResult RunAndRecordDetailed(ScenarioConfig cfg, Stream replayOut)
    {
        ArgumentNullException.ThrowIfNull(replayOut);
        ReplayHeader header = ReplayHeader.FromScenarioConfig(cfg);
        using ReplayWriter writer = new(replayOut, header);
        return RunDetailedInternal(cfg, writer);
    }

    public ScenarioResult RunDetailed(ScenarioConfig cfg) => RunDetailedInternal(cfg, replayWriter: null);

    private ScenarioResult RunDetailedInternal(ScenarioConfig cfg, ReplayWriter? replayWriter)
    {
        Validate(cfg);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
        Fix32 visionRadius = Fix32.FromInt(cfg.VisionRadiusTiles);
        ServerConfig serverConfig = ServerConfig.Default(cfg.ServerSeed) with
        {
            SnapshotEveryTicks = cfg.SnapshotEveryTicks,
            ZoneCount = cfg.ZoneCount,
            NpcCountPerZone = cfg.NpcCount,
            VisionRadius = visionRadius,
            VisionRadiusSq = visionRadius * visionRadius
        };

        _logger.LogInformation(BotRunnerLogEvents.ScenarioStart, "ScenarioStart bots={Bots} ticks={Ticks} seed={Seed}", cfg.BotCount, cfg.TickCount, cfg.ServerSeed);

        using ScenarioChecksumBuilder checksum = new();
        List<BotClient> clients = new(cfg.BotCount);
        Dictionary<(int BotIndex, int Tick), Snapshot> committedSnapshots = new();
        Dictionary<int, SortedDictionary<int, Snapshot>> pendingSnapshotsByBot = new();
        ServerHost host = new(serverConfig, LoggerFactory);
        int invariantFailures = 0;

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

            DrainAllMessagesForTests(clients);
            foreach (SortedDictionary<int, Snapshot> pending in pendingSnapshotsByBot.Values)
            {
                pending.Clear();
            }

            ReplayMove[] tickMoves = new ReplayMove[cfg.BotCount];
            int[] lastSentInputTickByBot = new int[cfg.BotCount];

            for (int tick = 1; tick <= cfg.TickCount; tick++)
            {
                DrainSnapshotsForTests(clients, pendingSnapshotsByBot);

                foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                {
                    BotDecision decision = agents[client.BotIndex].Decide(client);
                    int snapshotTick = client.LastSnapshot?.Tick ?? tick;
                    int commandTick = Math.Max(lastSentInputTickByBot[client.BotIndex] + 1, snapshotTick);
                    client.SendInput(commandTick, decision.MoveX, decision.MoveY);
                    lastSentInputTickByBot[client.BotIndex] = commandTick;

                    if (decision.AttackTargetId is int targetId && client.EntityId is int attackerId)
                    {
                        client.SendAttackIntent(commandTick, attackerId, targetId);
                    }

                    tickMoves[client.BotIndex] = new ReplayMove(decision.MoveX, decision.MoveY);
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

            BotStats[] stats = clients
                .OrderBy(client => client.BotIndex)
                .Select(client => new BotStats(
                    BotIndex: client.BotIndex,
                    SnapshotsReceived: client.SnapshotsReceived,
                    Errors: 0))
                .ToArray();

            foreach (BotStats stat in stats)
            {
                if (stat.SnapshotsReceived <= 0)
                {
                    invariantFailures++;
                    _logger.LogWarning(BotRunnerLogEvents.InvariantFailed, "InvariantFailed botIndex={BotIndex} reason=snapshots_received_zero", stat.BotIndex);
                }

                _logger.LogInformation(BotRunnerLogEvents.BotSummary, "BotSummary botIndex={BotIndex} snapshots={Snapshots} errors={Errors}", stat.BotIndex, stat.SnapshotsReceived, stat.Errors);
            }

            ServerMetricsSnapshot metrics = host.SnapshotMetrics();
            double tickAvgMs = metrics.TickAvgMsWindow > 0 ? metrics.TickAvgMsWindow : double.Epsilon;
            double tickP95Ms = metrics.TickP95MsWindow >= tickAvgMs ? metrics.TickP95MsWindow : tickAvgMs;

            _logger.LogInformation(BotRunnerLogEvents.ScenarioEnd, "ScenarioEnd checksum={Checksum} ticks={Ticks}", finalChecksum, cfg.TickCount);

            return new ScenarioResult(
                Checksum: finalChecksum,
                Ticks: cfg.TickCount,
                Bots: cfg.BotCount,
                MessagesIn: metrics.MessagesInTotal,
                MessagesOut: metrics.MessagesOutTotal,
                TickAvgMs: tickAvgMs,
                TickP95Ms: tickP95Ms,
                PlayersConnectedMax: metrics.PlayersConnected,
                BotStats: stats,
                InvariantFailures: invariantFailures);
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
        const int maxBufferPerBot = 64;

        for (int spin = 0; spin < maxBufferPerBot; spin++)
        {
            bool hadAnyMessage = false;

            foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
            {
                SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[client.BotIndex];
                client.PumpMessages(message =>
                {
                    if (message is Snapshot snapshot)
                    {
                        pending[snapshot.Tick] = snapshot;
                        hadAnyMessage = true;
                    }
                });

                foreach (int staleTick in pending.Keys.Where(t => t < expectedSnapshotTick).ToList())
                {
                    pending.Remove(staleTick);
                }

                while (pending.Count > maxBufferPerBot)
                {
                    int oldest = pending.Keys.Min();
                    pending.Remove(oldest);
                }
            }

            bool allHaveAtLeastExpectedOrAhead = clients
                .OrderBy(c => c.BotIndex)
                .All(client =>
                {
                    SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[client.BotIndex];
                    return pending.Count > 0 && pending.Keys.Min() >= expectedSnapshotTick;
                });

            if (allHaveAtLeastExpectedOrAhead)
            {
                foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
                {
                    SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[client.BotIndex];
                    int minTick = pending.Keys.Min();
                    if (minTick > expectedSnapshotTick)
                    {
                        throw new InvalidOperationException(
                            $"Unable to synchronize snapshots (expectedSnapshotTick={expectedSnapshotTick}, bot={client.BotIndex} missed expected tick, have minTick={minTick}).");
                    }

                    if (!pending.ContainsKey(expectedSnapshotTick))
                    {
                        throw new InvalidOperationException(
                            $"Unable to synchronize snapshots (expectedSnapshotTick={expectedSnapshotTick}, bot={client.BotIndex} missing exact expected tick).");
                    }
                }

                return expectedSnapshotTick;
            }

            if (!hadAnyMessage)
            {
                break;
            }
        }

        string perBot = string.Join(", ",
            clients
                .OrderBy(c => c.BotIndex)
                .Select(c =>
                {
                    SortedDictionary<int, Snapshot> pending = pendingSnapshotsByBot[c.BotIndex];
                    int min = pending.Count == 0 ? 0 : pending.Keys.Min();
                    int max = pending.Count == 0 ? 0 : pending.Keys.Max();
                    return $"bot{c.BotIndex}:count={pending.Count},min={min},max={max}";
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

        if (cfg.ZoneCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cfg), "ZoneCount must be > 0.");
        }

        if (cfg.VisionRadiusTiles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cfg), "VisionRadiusTiles must be >= 0.");
        }
    }
}
