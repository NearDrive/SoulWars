using Game.Audit;
using Game.Core;
using Game.Persistence.Sqlite;
using Game.Protocol;
using Game.Server;

namespace Game.Server.Tests;

internal static class DoDRunner
{
    internal static DoDRunResult Run(DoDRunConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        InMemoryAuditSink? auditSink = config.EnableAudit ? new InMemoryAuditSink() : null;
        ServerHost host = new(CreateServerConfig(config), auditSink: auditSink);
        List<RunnerBot> bots = ConnectBots(host, config.ZoneCount, config.BotCount, config.AccountPrefix);

        int invariantFailures = 0;
        try
        {
            for (int tick = 1; tick <= config.TickCount; tick++)
            {
                foreach (RunnerBot bot in bots)
                {
                    sbyte moveX = DeterministicAxis(tick, bot.BotIndex, salt: 19);
                    sbyte moveY = DeterministicAxis(tick, bot.BotIndex, salt: 73);
                    bot.Endpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(tick, moveX, moveY)));
                }

                host.StepOnce();

                foreach (RunnerBot bot in bots)
                {
                    DrainMessages(bot.Endpoint, message => config.OnServerMessage?.Invoke(tick, bot, message));
                }
            }
        }
        catch (InvariantViolationException)
        {
            invariantFailures++;
            throw;
        }

        PerfSnapshot perfSnapshot = host.SnapshotAndResetPerfWindow();
        string checksum = StateChecksum.Compute(host.CurrentWorld);
        int auditEventCount = auditSink?.Events.Count ?? 0;
        int auditLastTick = auditEventCount > 0 ? auditSink!.Events[auditEventCount - 1].Header.Tick : 0;
        AuditSummary auditSummary = new(
            Events: auditEventCount,
            LastTick: auditLastTick);

        return new DoDRunResult(
            Checksum: checksum,
            InvariantFailures: invariantFailures,
            PerfSnapshot: perfSnapshot,
            AuditSummary: auditSummary,
            ProtocolVersion: ProtocolConstants.CurrentProtocolVersion,
            SchemaVersion: SqliteSchema.CurrentVersion,
            World: host.CurrentWorld,
            BotStates: bots,
            Diagnostic: $"seed={config.Seed} zones={config.ZoneCount} bots={config.BotCount} ticks={config.TickCount} checksum={checksum}");
    }

    internal static ServerConfig CreateServerConfig(DoDRunConfig config)
    {
        ServerConfig baseConfig = ServerConfig.Default(config.Seed) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = config.ZoneCount,
            NpcCountPerZone = config.NpcCount,
            EnableMetrics = true
        };

        return config.ServerConfigOverride?.Invoke(baseConfig) ?? baseConfig;
    }

    internal static List<RunnerBot> ConnectBots(ServerHost host, int zoneCount, int botCount, string accountPrefix)
    {
        List<RunnerBot> bots = new(botCount);

        for (int i = 0; i < botCount; i++)
        {
            InMemoryEndpoint endpoint = new();
            host.Connect(endpoint);

            int zoneId = (i % zoneCount) + 1;
            string accountId = $"{accountPrefix}-{i:D3}";
            endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, accountId)));
            bots.Add(new RunnerBot(i, accountId, zoneId, endpoint));
        }

        host.ProcessInboundOnce();

        foreach (RunnerBot bot in bots)
        {
            DrainMessages(bot.Endpoint, _ => { });
            bot.Endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(bot.ZoneId)));
            bot.Endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(bot.ZoneId, 0)));
        }

        host.ProcessInboundOnce();
        foreach (RunnerBot bot in bots)
        {
            DrainMessages(bot.Endpoint, _ => { });
        }

        return bots;
    }

    internal static void DrainMessages(InMemoryEndpoint endpoint, Action<IServerMessage> onMessage)
    {
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out ProtocolErrorCode error) &&
                error == ProtocolErrorCode.None &&
                message is not null)
            {
                onMessage(message);
            }
        }
    }

    private static sbyte DeterministicAxis(int tick, int botIndex, int salt)
    {
        int value = ((tick * 31) + (botIndex * 17) + salt) % 3;
        return value switch
        {
            0 => (sbyte)-1,
            1 => (sbyte)0,
            _ => (sbyte)1
        };
    }
}

internal sealed record DoDRunConfig(
    int Seed,
    int ZoneCount,
    int BotCount,
    int TickCount,
    int NpcCount = 0,
    bool EnableAudit = false,
    string AccountPrefix = "dod-bot",
    Func<ServerConfig, ServerConfig>? ServerConfigOverride = null,
    Action<int, RunnerBot, IServerMessage>? OnServerMessage = null);

internal sealed record DoDRunResult(
    string Checksum,
    int InvariantFailures,
    PerfSnapshot PerfSnapshot,
    AuditSummary AuditSummary,
    int ProtocolVersion,
    int SchemaVersion,
    WorldState World,
    IReadOnlyList<RunnerBot> BotStates,
    string Diagnostic);

internal sealed record RunnerBot(int BotIndex, string AccountId, int ZoneId, InMemoryEndpoint Endpoint);

internal sealed record AuditSummary(int Events, int LastTick);
