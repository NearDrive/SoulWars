using System.Collections.Immutable;
using Game.Core;
using Game.Persistence.Sqlite;
using Game.Protocol;
using Game.Server;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Game.Server.Tests.Mvp9;

public sealed class Mvp9InvariantsTests
{
    [Fact]
    public void Invariant_EntityIdUniqueAcrossZones()
    {
        EntityState shared = CreateEntity(101, 2, 2);
        WorldState world = new(
            Tick: 77,
            Zones: ImmutableArray.Create(
                new ZoneState(new ZoneId(1), BuildOpenMap(8, 8), ImmutableArray.Create(shared)),
                new ZoneState(new ZoneId(2), BuildOpenMap(8, 8), ImmutableArray.Create(shared with { Pos = new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(3)) }))),
            EntityLocations: ImmutableArray.Create(
                new EntityLocation(shared.Id, new ZoneId(1)),
                new EntityLocation(shared.Id, new ZoneId(2))),
            LootEntities: ImmutableArray<LootEntityState>.Empty);

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(() =>
            WorldInvariants.AssertNoEntityDupesAcrossZones(world, world.Tick));

        Assert.Contains("entityId=101", ex.Message, StringComparison.Ordinal);
        Assert.Contains("firstZoneId=1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("secondZoneId=2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("tick=77", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invariant_CanonicalOrdering_SnapshotListsAreSorted()
    {
        ServerHost host = new(ServerConfig.Default(seed: 52001) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 2,
            NpcCountPerZone = 0
        });

        InMemoryEndpoint endpoint1 = new();
        InMemoryEndpoint endpoint2 = new();
        host.Connect(endpoint1);
        host.Connect(endpoint2);
        endpoint1.EnqueueToServer(ProtocolCodec.Encode(new Hello("v")));
        endpoint1.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        endpoint2.EnqueueToServer(ProtocolCodec.Encode(new Hello("v")));
        endpoint2.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(2)));
        host.AdvanceTicks(6);

        List<Snapshot> snapshots = new();
        while (endpoint1.TryDequeueToClient(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? msg, out _) && msg is Snapshot snapshot)
            {
                snapshots.Add(snapshot);
            }
        }

        while (endpoint2.TryDequeueToClient(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? msg, out _) && msg is Snapshot snapshot)
            {
                snapshots.Add(snapshot);
            }
        }

        Assert.NotEmpty(snapshots);

        foreach (IGrouping<int, Snapshot> tickGroup in snapshots.GroupBy(s => s.Tick))
        {
            WorldInvariants.AssertSortedAscending(tickGroup.OrderBy(s => s.ZoneId).ToArray(), s => s.ZoneId, "snapshots.zones", tickGroup.Key);
        }

        foreach (Snapshot snapshot in snapshots)
        {
            Assert.Equal(snapshot.Entities.OrderBy(e => e.EntityId).Select(e => e.EntityId), snapshot.Entities.Select(e => e.EntityId));
            WorldInvariants.AssertSortedAscending(snapshot.Entities, e => e.EntityId, "snapshot.entities", snapshot.Tick, snapshot.ZoneId);
        }


        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(() =>
            WorldInvariants.AssertSortedAscending(new[] { 9, 4, 12 }, x => x, "snapshot.entities", tick: 9, zoneId: 1));

        Assert.Contains("array=snapshot.entities", ex.Message, StringComparison.Ordinal);
        Assert.Contains("index=1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("prev=9", ex.Message, StringComparison.Ordinal);
        Assert.Contains("curr=4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invariant_ChecksumMismatch_LoadFailsFastWithExpectedActual()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"mvp9-checksum-{Guid.NewGuid():N}.db");

        try
        {
            ServerConfig cfg = ServerConfig.Default(seed: 8801) with
            {
                ZoneCount = 2,
                NpcCountPerZone = 0
            };

            ServerHost host = new(cfg);
            host.AdvanceTicks(10);

            SqliteGameStore store = new(dbPath);
            string checksum = StateChecksum.Compute(host.CurrentWorld);
            SnapshotMeta meta = SnapshotMetaBuilder.Create(host.CurrentWorld, cfg.ToSimulationConfig(), zoneDefinitions: null, buildHash: "mvp9");
            store.SaveWorld(host.CurrentWorld, cfg.Seed, Array.Empty<PlayerRecord>(), checksum, meta);

            CorruptLatestSnapshotChecksum(dbPath);

            SnapshotChecksumMismatchException ex = Assert.Throws<SnapshotChecksumMismatchException>(() => store.LoadWorld());
            Assert.Equal(checksum, ex.Expected);
            Assert.NotEqual(ex.Expected, ex.Actual);
            Assert.Contains($"expected={ex.Expected}", ex.Message, StringComparison.Ordinal);
            Assert.Contains($"actual={ex.Actual}", ex.Message, StringComparison.Ordinal);
            Assert.Contains("scope=global", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static void CorruptLatestSnapshotChecksum(string dbPath)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();

        using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT id, checksum FROM world_snapshots ORDER BY id DESC LIMIT 1;";

        using SqliteDataReader reader = select.ExecuteReader();
        Assert.True(reader.Read());

        long id = reader.GetInt64(0);
        string checksum = reader.GetString(1);
        reader.Close();

        string corruptedChecksum = checksum[0] == '0'
            ? "1" + checksum[1..]
            : "0" + checksum[1..];

        using SqliteCommand update = connection.CreateCommand();
        update.CommandText = "UPDATE world_snapshots SET checksum = $checksum WHERE id = $id;";
        update.Parameters.AddWithValue("$checksum", corruptedChecksum);
        update.Parameters.AddWithValue("$id", id);
        update.ExecuteNonQuery();
    }

    private static EntityState CreateEntity(int id, int x, int y)
        => new(
            Id: new EntityId(id),
            Pos: new Vec2Fix(Fix32.FromInt(x), Fix32.FromInt(y)),
            Vel: Vec2Fix.Zero,
            MaxHp: 10,
            Hp: 10,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 1,
            AttackCooldownTicks: 1,
            LastAttackTick: 0,
            Kind: EntityKind.Player);

    private static TileMap BuildOpenMap(int width, int height)
    {
        ImmutableArray<TileKind>.Builder tiles = ImmutableArray.CreateBuilder<TileKind>(width * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                tiles.Add(border ? TileKind.Solid : TileKind.Empty);
            }
        }

        return new TileMap(width, height, tiles.MoveToImmutable());
    }
}
