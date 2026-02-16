using System.Globalization;
using Game.App.Headless;
using Game.Core;
using Game.Persistence;
using Game.Persistence.Sqlite;
using Game.Protocol;
using Game.Server;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "DoD")]
public sealed class DoDGlobalValidationTests
{
    [Fact]
    public void DoD_MultiZone_TwoRuns_SameChecksum()
    {
        DoDRunConfig cfg = new(Seed: 5101, ZoneCount: 3, BotCount: 30, TickCount: 2000);

        DoDRunResult run1 = DoDRunner.Run(cfg);
        DoDRunResult run2 = DoDRunner.Run(cfg);

        EnsureEntityNotDuplicatedAcrossZones(run1.World);
        EnsureEntityNotDuplicatedAcrossZones(run2.World);

        Assert.True(string.Equals(run1.Checksum, run2.Checksum, StringComparison.Ordinal),
            BuildDiag("MultiZone checksum drift", cfg, run1, run2));
    }

    [Fact]
    public void DoD_AOI_StableVisibleSet_TwoRuns()
    {
        const int viewerBotIndex = 0;
        List<int> finalVisibleRun1 = new();
        List<int> finalVisibleRun2 = new();

        DoDRunConfig cfg1 = new(Seed: 5102, ZoneCount: 2, BotCount: 10, TickCount: 600,
            OnServerMessage: (_, bot, message) =>
            {
                if (bot.BotIndex == viewerBotIndex && message is Snapshot snapshot)
                {
                    finalVisibleRun1.Clear();
                    finalVisibleRun1.AddRange(snapshot.Entities.Select(entity => entity.EntityId));
                }
            });

        DoDRunConfig cfg2 = cfg1 with
        {
            OnServerMessage = (_, bot, message) =>
            {
                if (bot.BotIndex == viewerBotIndex && message is Snapshot snapshot)
                {
                    finalVisibleRun2.Clear();
                    finalVisibleRun2.AddRange(snapshot.Entities.Select(entity => entity.EntityId));
                }
            }
        };

        DoDRunResult run1 = DoDRunner.Run(cfg1);
        DoDRunResult run2 = DoDRunner.Run(cfg2);

        Assert.NotEmpty(finalVisibleRun1);
        Assert.Equal(finalVisibleRun1, finalVisibleRun2);
        Assert.Equal(finalVisibleRun1.Count, finalVisibleRun1.Distinct().Count());

        int viewerZone = (viewerBotIndex % cfg1.ZoneCount) + 1;
        HashSet<int> worldIds = run1.World.Zones.Single(zone => zone.Id.Value == viewerZone).Entities
            .Select(entity => entity.Id.Value)
            .ToHashSet();
        Assert.True(finalVisibleRun1.All(worldIds.Contains),
            BuildDiag("AOI visible-set contains ids not present in final snapshot zone", cfg1, run1, run2));
    }

    [Fact]
    public void DoD_SnapshotSeq_Resend_OnDrop()
    {
        const int dropSeq = 3;
        const int retryLimit = 8;

        ServerConfig cfg = ServerConfig.Default(5103) with
        {
            SnapshotEveryTicks = 1,
            SnapshotRetryLimit = retryLimit
        };

        ServerHost host = new(cfg);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "seq-dod")));
        host.ProcessInboundOnce();
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        host.ProcessInboundOnce();

        List<int> observedSeq = new();
        bool droppedOnce = false;
        int droppedSeqReceiveCount = 0;

        Exception? stepError = Record.Exception(() =>
        {
            for (int tick = 1; tick <= 12; tick++)
            {
                host.StepOnce();
                while (endpoint.TryDequeueFromServer(out byte[] payload))
                {
                    if (!ProtocolCodec.TryDecodeServer(payload, out IServerMessage? msg, out _) || msg is null)
                    {
                        continue;
                    }

                    if (msg is not SnapshotV2 snapshot)
                    {
                        continue;
                    }

                    observedSeq.Add(snapshot.SnapshotSeq);
                    if (snapshot.SnapshotSeq == dropSeq)
                    {
                        droppedSeqReceiveCount++;
                    }

                    if (snapshot.SnapshotSeq == dropSeq && !droppedOnce)
                    {
                        droppedOnce = true;
                        continue;
                    }

                    endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq)));
                }
            }
        });

        Assert.Null(stepError);
        Assert.True(droppedOnce, "drop sequence was not observed");
        Assert.True(droppedSeqReceiveCount >= 2, "expected at least one resend for dropped seq");
        Assert.True(IsMonotonicNonDecreasing(observedSeq), $"seq stream not monotonic: {string.Join(',', observedSeq)}");
        Assert.True(droppedSeqReceiveCount <= retryLimit,
            $"retry limit exceeded. droppedSeqReceiveCount={droppedSeqReceiveCount} retryLimit={retryLimit}");
        Assert.False(endpoint.IsClosed);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void DoD_PerfBudgets_ReferenceScenario_WithinLimits()
    {
        DoDRunConfig cfg = new(Seed: 2025, ZoneCount: 3, BotCount: 50, TickCount: 2000);
        DoDRunResult run = DoDRunner.Run(cfg);

        BudgetResult budget = PerfBudgetEvaluator.Evaluate(run.PerfSnapshot, PerfBudgetConfig.Default);
        Assert.True(budget.Ok,
            $"Perf budget failed. {run.Diagnostic} maxAoi={run.PerfSnapshot.MaxAoiDistanceChecksPerTick} maxCollision={run.PerfSnapshot.MaxCollisionChecksPerTick} maxOutbound={run.PerfSnapshot.MaxOutboundBytesPerTick} violations={string.Join(";", budget.Violations)}");
    }

    [Fact]
    public void DoD_Config_SameArgs_SameChecksum()
    {
        ServerAppConfig cfg = new(
            Seed: 5205,
            Port: 7777,
            SqlitePath: null,
            ZoneCount: 3,
            BotCount: 20);

        ServerAppConfig before = cfg;
        RunResult run1 = Program.RunOnce(cfg, ticks: 1000);
        RunResult run2 = Program.RunOnce(cfg, ticks: 1000);

        Assert.Equal(TestChecksum.NormalizeFullHex(run1.Checksum), TestChecksum.NormalizeFullHex(run2.Checksum));
        Assert.Equal(before, cfg);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void DoD_Persistence_Migrate_Load_Continue_NoDrift()
    {
        const int seed = 5306;
        const int zoneCount = 2;
        const int botCount = 10;

        string dbPath = Path.Combine(Path.GetTempPath(), $"soulwars-dod-v1-{Guid.NewGuid():N}.sqlite");
        try
        {
            ServerConfig cfg = ServerConfig.Default(seed) with { ZoneCount = zoneCount, SnapshotEveryTicks = 1 };
            ServerHost initial = new(cfg);
            byte[] initialWorldBytes = WorldStateSerializer.SaveToBytes(initial.CurrentWorld);
            CreateV1FixtureDb(dbPath, seed, initial.CurrentWorld.Tick, initialWorldBytes, StateChecksum.Compute(initial.CurrentWorld));

            SqliteMigrator.InitializeOrMigrate(dbPath);

            ServerHost resumed = ServerHost.LoadFromSqlite(cfg, dbPath);
            List<RunnerBot> resumedBots = DoDRunner.ConnectBots(resumed, zoneCount, botCount, "persist");
            RunTicks(resumed, resumedBots, fromTick: 1, toTick: 500);
            resumed.SaveToSqlite(dbPath);

            ServerHost restarted = ServerHost.LoadFromSqlite(cfg, dbPath);
            List<RunnerBot> restartedBots = DoDRunner.ConnectBots(restarted, zoneCount, botCount, "persist");
            RunTicks(restarted, restartedBots, fromTick: 501, toTick: 1000);
            string migratedChecksum = StateChecksum.Compute(restarted.CurrentWorld);

            ServerHost baseline = new(cfg);
            List<RunnerBot> baselineBots = DoDRunner.ConnectBots(baseline, zoneCount, botCount, "persist");
            RunTicks(baseline, baselineBots, fromTick: 1, toTick: 1000);
            string baselineChecksum = StateChecksum.Compute(baseline.CurrentWorld);

            Assert.Equal(baselineChecksum, migratedChecksum);
            Assert.Equal(SqliteSchema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DoD_Hardening_AbuseGuards_Work_And_NoChecksumImpact()
    {
        ServerConfig hardening = ServerConfig.Default(seed: 5407) with
        {
            MaxMsgsPerTick = 1,
            MaxBytesPerTick = 256,
            AbuseStrikesToDeny = 3,
            AbuseWindowTicks = 100,
            DenyTicks = 1000
        };

        ServerHost host = new(hardening);
        for (int strike = 0; strike < 3; strike++)
        {
            DeterministicEndpoint attacker = new("abuse-ip");
            Assert.True(host.TryConnect(attacker, out _, out _));
            attacker.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("bot", "abuse")));
            attacker.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("bot", "abuse")));
            host.ProcessInboundOnce();

            Disconnect disconnect = ReadDisconnect(attacker);
            Assert.Equal(DisconnectReason.RateLimitExceeded, disconnect.Reason);
            host.AdvanceTicks(1);
        }

        DeterministicEndpoint denied = new("abuse-ip");
        Assert.False(host.TryConnect(denied, out _, out DisconnectReason? denyReason));
        Assert.Equal(DisconnectReason.DenyListed, denyReason);
        Assert.Equal(DisconnectReason.DenyListed, ReadDisconnect(denied).Reason);

        ServerConfig hardeningOn = ServerConfig.Default(seed: 5407);
        ServerConfig hardeningOff = hardeningOn with
        {
            MaxConcurrentSessions = int.MaxValue,
            MaxConnectionsPerIp = int.MaxValue,
            MaxMsgsPerTick = int.MaxValue,
            MaxBytesPerTick = int.MaxValue,
            AbuseStrikesToDeny = int.MaxValue,
            AbuseWindowTicks = int.MaxValue,
            DenyTicks = int.MaxValue
        };

        DoDRunConfig scenario = new(Seed: hardeningOn.Seed, ZoneCount: 2, BotCount: 12, TickCount: 1000,
            ServerConfigOverride: cfg => cfg with
            {
                MaxConcurrentSessions = hardeningOn.MaxConcurrentSessions,
                MaxConnectionsPerIp = hardeningOn.MaxConnectionsPerIp,
                MaxMsgsPerTick = hardeningOn.MaxMsgsPerTick,
                MaxBytesPerTick = hardeningOn.MaxBytesPerTick,
                AbuseStrikesToDeny = hardeningOn.AbuseStrikesToDeny,
                AbuseWindowTicks = hardeningOn.AbuseWindowTicks,
                DenyTicks = hardeningOn.DenyTicks
            });

        DoDRunConfig scenarioOff = scenario with
        {
            ServerConfigOverride = cfg => cfg with
            {
                MaxConcurrentSessions = hardeningOff.MaxConcurrentSessions,
                MaxConnectionsPerIp = hardeningOff.MaxConnectionsPerIp,
                MaxMsgsPerTick = hardeningOff.MaxMsgsPerTick,
                MaxBytesPerTick = hardeningOff.MaxBytesPerTick,
                AbuseStrikesToDeny = hardeningOff.AbuseStrikesToDeny,
                AbuseWindowTicks = hardeningOff.AbuseWindowTicks,
                DenyTicks = hardeningOff.DenyTicks
            }
        };

        DoDRunResult runOn = DoDRunner.Run(scenario);
        DoDRunResult runOff = DoDRunner.Run(scenarioOff);

        Assert.Equal(runOff.Checksum, runOn.Checksum);
    }

    [Fact]
    public void DoD_DriftGate_TwoRuns_SameChecksum()
    {
        DoDRunConfig cfg = new(Seed: 5508, ZoneCount: 2, BotCount: 10, TickCount: 1000);
        DoDRunResult run1 = DoDRunner.Run(cfg);
        DoDRunResult run2 = DoDRunner.Run(cfg);

        Assert.Equal(run1.Checksum, run2.Checksum);
    }

    private static void RunTicks(ServerHost host, IReadOnlyList<RunnerBot> bots, int fromTick, int toTick)
    {
        for (int tick = fromTick; tick <= toTick; tick++)
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
                DoDRunner.DrainMessages(bot.Endpoint, _ => { });
            }
        }
    }

    private static void EnsureEntityNotDuplicatedAcrossZones(WorldState world)
    {
        HashSet<int> ids = new();
        foreach (ZoneState zone in world.Zones)
        {
            foreach (EntityState entity in zone.Entities)
            {
                Assert.True(ids.Add(entity.Id.Value), $"entity duplicated across zones. entityId={entity.Id.Value}");
            }
        }
    }

    private static bool IsMonotonicNonDecreasing(IReadOnlyList<int> values)
    {
        for (int i = 1; i < values.Count; i++)
        {
            if (values[i] < values[i - 1])
            {
                return false;
            }
        }

        return true;
    }

    private static int ReadSchemaVersion(string dbPath)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key='schema_version';";
        string? raw = command.ExecuteScalar()?.ToString();
        return int.Parse(raw ?? "0", CultureInfo.InvariantCulture);
    }

    private static void CreateV1FixtureDb(string dbPath, int serverSeed, int tick, byte[] worldBlob, string checksum)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
INSERT INTO meta (key, value) VALUES ('schema_version', '1');
CREATE TABLE world_snapshots (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    saved_at_tick INTEGER NOT NULL,
    world_blob BLOB NOT NULL,
    checksum TEXT NOT NULL
);
CREATE TABLE players (
    account_id TEXT PRIMARY KEY,
    player_id INTEGER NOT NULL,
    entity_id INTEGER NULL,
    zone_id INTEGER NULL
);
INSERT INTO world_snapshots(id, saved_at_tick, world_blob, checksum)
VALUES (1, $tick, $worldBlob, $checksum);
INSERT INTO players(account_id, player_id, entity_id, zone_id)
VALUES ('seed-player', 1, NULL, NULL);
INSERT INTO meta(key, value)
VALUES ('server_seed', $serverSeed);";
        command.Parameters.AddWithValue("$tick", tick);
        command.Parameters.AddWithValue("$worldBlob", worldBlob);
        command.Parameters.AddWithValue("$checksum", checksum);
        command.Parameters.AddWithValue("$serverSeed", serverSeed.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static Disconnect ReadDisconnect(DeterministicEndpoint endpoint)
    {
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? msg, out _) && msg is Disconnect disconnect)
            {
                return disconnect;
            }
        }

        throw new Xunit.Sdk.XunitException("disconnect not found");
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

    private static string BuildDiag(string title, DoDRunConfig cfg, DoDRunResult run1, DoDRunResult run2)
        =>
            $"{title} | seed={cfg.Seed} zones={cfg.ZoneCount} bots={cfg.BotCount} ticks={cfg.TickCount} checksum1={run1.Checksum} checksum2={run2.Checksum} inv1={run1.InvariantFailures} inv2={run2.InvariantFailures} perf1(maxAoi={run1.PerfSnapshot.MaxAoiDistanceChecksPerTick},maxCollision={run1.PerfSnapshot.MaxCollisionChecksPerTick},maxOutbound={run1.PerfSnapshot.MaxOutboundBytesPerTick}) perf2(maxAoi={run2.PerfSnapshot.MaxAoiDistanceChecksPerTick},maxCollision={run2.PerfSnapshot.MaxCollisionChecksPerTick},maxOutbound={run2.PerfSnapshot.MaxOutboundBytesPerTick}) protocol={run1.ProtocolVersion} schema={run1.SchemaVersion}";

    private sealed class DeterministicEndpoint : IServerEndpoint, IClientEndpoint
    {
        private readonly Queue<byte[]> _toServer = new();
        private readonly Queue<byte[]> _toClient = new();

        public DeterministicEndpoint(string endpointKey)
        {
            EndpointKey = endpointKey;
        }

        public string EndpointKey { get; }

        public bool IsClosed { get; private set; }

        public bool TryDequeueToServer(out byte[] msg)
        {
            if (_toServer.Count == 0)
            {
                msg = Array.Empty<byte>();
                return false;
            }

            msg = _toServer.Dequeue();
            return true;
        }

        public void EnqueueToClient(byte[] msg)
        {
            if (!IsClosed)
            {
                _toClient.Enqueue(msg);
            }
        }

        public void Close() => IsClosed = true;

        public void EnqueueToServer(byte[] msg)
        {
            if (!IsClosed)
            {
                _toServer.Enqueue(msg);
            }
        }

        public bool TryDequeueFromServer(out byte[] msg)
        {
            if (_toClient.Count == 0)
            {
                msg = Array.Empty<byte>();
                return false;
            }

            msg = _toClient.Dequeue();
            return true;
        }
    }
}
