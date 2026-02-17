using System.Collections.Immutable;
using Game.Audit;
using Game.Core;
using Game.Persistence;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "Persistence")]
public sealed class PersistenceConsistencyIntegrationTests
{
    [Fact]
    public void SaveLoadContinue_Equals_DirectRun_NoIdDrift()
    {
        const int seed = 9028;
        const int botCount = 10;
        const int splitTick = 500;
        const int totalTicks = 1000;

        RunResult baseline = RunDirect(seed, botCount, totalTicks);

        ServerConfig config = ServerConfig.Default(seed) with { SnapshotEveryTicks = 1, ZoneCount = 2, NpcCountPerZone = 2 };
        InMemoryAuditSink auditA = new();
        ServerHost first = new(config, auditSink: auditA);
        InMemoryEndpoint[] firstEndpoints = ConnectBots(first, botCount);

        RunTicks(first, firstEndpoints, 1, splitTick);
        AssertExtendedState(first, splitTick, expectedEntityCount: first.WorldEntityCountTotal);

        Snapshot[] firstHalfSnapshots = firstEndpoints.SelectMany(DrainSnapshots).ToArray();
        int[] idsAtSave = CollectEntityIds(first.CurrentWorld);
        byte[] bytes = WorldStateSerializer.SaveToBytes(first.CurrentWorld);

        ServerBootstrap bootstrap = new(
            WorldStateSerializer.LoadFromBytes(bytes),
            config.Seed,
            first.GetPlayersSnapshot()
                .Select(p => new BootstrapPlayerRecord(p.AccountId, p.PlayerId.Value, p.EntityId, p.ZoneId, ImmutableArray<ItemStack>.Empty))
                .ToImmutableArray());

        InMemoryAuditSink auditB = new();
        ServerHost second = new(config, bootstrap: bootstrap, auditSink: auditB);
        InMemoryEndpoint[] secondEndpoints = ConnectBots(second, botCount);

        Assert.Equal(idsAtSave, CollectEntityIds(second.CurrentWorld));

        RunTicks(second, secondEndpoints, splitTick + 1, totalTicks);
        AssertExtendedState(second, totalTicks, expectedEntityCount: baseline.EntityCount);

        string continuedChecksum = StateChecksum.Compute(second.CurrentWorld);
        Snapshot[] secondHalfSnapshots = secondEndpoints.SelectMany(DrainSnapshots).ToArray();
        string continuedSnapshotHash = SnapshotHash.ComputeHex(firstHalfSnapshots.Concat(secondHalfSnapshots));

        Assert.Equal(baseline.WorldChecksum, continuedChecksum);
        Assert.Equal(baseline.SnapshotHash, continuedSnapshotHash);
        AssertAuditOrdered(auditA.Events.Concat(auditB.Events).ToArray());
    }

    [Fact]
    public void RestartSqliteLoad_Equals_DirectRun_NoIdDrift()
    {
        const int seed = 9128;
        const int botCount = 10;
        const int splitTick = 500;
        const int totalTicks = 1000;

        RunResult baseline = RunDirect(seed, botCount, totalTicks);

        string dbPath = CreateTempDbPath();
        try
        {
            ServerConfig config = ServerConfig.Default(seed) with { SnapshotEveryTicks = 1, ZoneCount = 2, NpcCountPerZone = 2 };
            InMemoryAuditSink auditA = new();
            ServerHost first = new(config, auditSink: auditA);
            InMemoryEndpoint[] firstEndpoints = ConnectBots(first, botCount);

            RunTicks(first, firstEndpoints, 1, splitTick);
            AssertExtendedState(first, splitTick, expectedEntityCount: first.WorldEntityCountTotal);
            Snapshot[] firstHalfSnapshots = firstEndpoints.SelectMany(DrainSnapshots).ToArray();
            int[] idsAtSave = CollectEntityIds(first.CurrentWorld);

            first.SaveToSqlite(dbPath);

            ServerHost second = ServerHost.LoadFromSqlite(config, dbPath, metrics: null);
            InMemoryEndpoint[] secondEndpoints = ConnectBots(second, botCount);

            Assert.Equal(idsAtSave, CollectEntityIds(second.CurrentWorld));

            RunTicks(second, secondEndpoints, splitTick + 1, totalTicks);
            AssertExtendedState(second, totalTicks, expectedEntityCount: baseline.EntityCount);

            string continuedChecksum = StateChecksum.Compute(second.CurrentWorld);
            Snapshot[] secondHalfSnapshots = secondEndpoints.SelectMany(DrainSnapshots).ToArray();
            string continuedSnapshotHash = SnapshotHash.ComputeHex(firstHalfSnapshots.Concat(secondHalfSnapshots));

            Assert.Equal(baseline.WorldChecksum, continuedChecksum);
            Assert.Equal(baseline.SnapshotHash, continuedSnapshotHash);
            AssertAuditOrdered(auditA.Events.ToArray());
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    private static RunResult RunDirect(int seed, int botCount, int totalTicks)
    {
        ServerConfig config = ServerConfig.Default(seed) with { SnapshotEveryTicks = 1, ZoneCount = 2, NpcCountPerZone = 2 };
        InMemoryAuditSink audit = new();
        ServerHost host = new(config, auditSink: audit);
        InMemoryEndpoint[] endpoints = ConnectBots(host, botCount);

        RunTicks(host, endpoints, 1, totalTicks);
        AssertExtendedState(host, totalTicks, expectedEntityCount: host.WorldEntityCountTotal);

        return new RunResult(
            StateChecksum.Compute(host.CurrentWorld),
            SnapshotHash.ComputeHex(endpoints.SelectMany(DrainSnapshots)),
            host.WorldEntityCountTotal);
    }

    private static InMemoryEndpoint[] ConnectBots(ServerHost host, int botCount)
    {
        InMemoryEndpoint[] endpoints = new InMemoryEndpoint[botCount];

        for (int i = 0; i < botCount; i++)
        {
            InMemoryEndpoint endpoint = new();
            endpoints[i] = endpoint;
            host.Connect(endpoint);

            string accountId = $"bot-{i:D2}";
            endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("bot", accountId)));
        }

        host.ProcessInboundOnce();

        for (int i = 0; i < botCount; i++)
        {
            ReadAllMessages(endpoints[i]);
            endpoints[i].EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        }

        host.ProcessInboundOnce();

        for (int i = 0; i < botCount; i++)
        {
            ReadAllMessages(endpoints[i]);
        }

        return endpoints;
    }

    private static void RunTicks(ServerHost host, InMemoryEndpoint[] endpoints, int startTickInclusive, int endTickInclusive)
    {
        for (int tick = startTickInclusive; tick <= endTickInclusive; tick++)
        {
            for (int botIndex = 0; botIndex < endpoints.Length; botIndex++)
            {
                sbyte moveX = DeterministicAxis(tick, botIndex, salt: 19);
                sbyte moveY = DeterministicAxis(tick, botIndex, salt: 73);
                endpoints[botIndex].EnqueueToServer(ProtocolCodec.Encode(new InputCommand(tick, moveX, moveY)));
            }

            host.StepOnce();
        }
    }

    private static Snapshot[] DrainSnapshots(InMemoryEndpoint endpoint)
    {
        List<Snapshot> snapshots = new();
        foreach (IServerMessage message in ReadAllMessages(endpoint))
        {
            if (message is Snapshot snapshot)
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots.ToArray();
    }

    private static IServerMessage[] ReadAllMessages(InMemoryEndpoint endpoint)
    {
        List<IServerMessage> messages = new();
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            messages.Add(ProtocolCodec.DecodeServer(payload));
        }

        return messages.ToArray();
    }

    private static void AssertExtendedState(ServerHost host, int tick, int expectedEntityCount)
    {
        CoreInvariants.Validate(host.CurrentWorld, tick);
        int[] entityIds = CollectEntityIds(host.CurrentWorld);
        Assert.Equal(expectedEntityCount, entityIds.Length);
        Assert.Equal(entityIds.Length, entityIds.Distinct().Count());

    }

    private static void AssertAuditOrdered(IReadOnlyList<AuditEvent> events)
    {
        for (int i = 1; i < events.Count; i++)
        {
            AuditEvent prev = events[i - 1];
            AuditEvent cur = events[i];
            Assert.True(cur.Header.Tick > prev.Header.Tick ||
                        (cur.Header.Tick == prev.Header.Tick && cur.Header.Seq > prev.Header.Seq));
        }
    }

    private static int[] CollectEntityIds(WorldState world)
        => world.Zones
            .SelectMany(zone => zone.Entities)
            .Select(entity => entity.Id.Value)
            .OrderBy(id => id)
            .ToArray();

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

    private static string CreateTempDbPath()
    {
        string fileName = $"soulwars-pr28-{Guid.NewGuid():N}.sqlite";
        return Path.Combine(Path.GetTempPath(), fileName);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record RunResult(string WorldChecksum, string SnapshotHash, int EntityCount);
}
