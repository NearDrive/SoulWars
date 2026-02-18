using System.Collections.Immutable;
using System.Security.Cryptography;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class AoiMvp9Tests
{
    [Fact]
    public void AOI_StableOrdering_NoDrift()
    {
        string run1 = CaptureSnapshotPayloadHash(seed: 9101);
        string run2 = CaptureSnapshotPayloadHash(seed: 9101);

        Assert.Equal(run1, run2);
    }

    [Fact]
    public void AOI_EntersLeaves_Deterministic()
    {
        Fix32 radius = Fix32.FromInt(2);
        ServerHost host = new(ServerConfig.Default(9102) with
        {
            SnapshotEveryTicks = 1,
            VisionRadius = radius,
            VisionRadiusSq = radius * radius,
            MaxEntitiesPerSnapshot = 32
        });

        InMemoryEndpoint endpointA = new();
        InMemoryEndpoint endpointB = new();
        host.Connect(endpointA);
        host.Connect(endpointB);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("A", "aoi-enter-a")));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("B", "aoi-enter-b")));
        endpointA.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
        endpointA.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));

        host.AdvanceTicks(2);

        (EnterZoneAck ackA, SnapshotV2 initialA) = ReadEnterAckAndLastSnapshotV2(endpointA);
        (EnterZoneAck ackB, SnapshotV2 _) = ReadEnterAckAndLastSnapshotV2(endpointB);
        int aId = ackA.EntityId;
        int bId = ackB.EntityId;

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(initialA.Tick + 1, 1, 0)));
        for (int i = 0; i < 32; i++)
        {
            host.StepOnce();
            SnapshotV2 snapB = ReadLastSnapshotV2(endpointB);
            endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, snapB.SnapshotSeq)));
            if (snapB.Enters.Any(e => e.EntityId == aId))
            {
                Assert.DoesNotContain(snapB.Leaves, id => id == aId);
                Assert.Equal(snapB.Enters.OrderBy(e => e.EntityId).Select(e => e.EntityId), snapB.Enters.Select(e => e.EntityId));
                break;
            }

            endpointA.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(snapB.Tick + 1, 1, 0)));
        }

        bool sawLeave = false;
        for (int i = 0; i < 64; i++)
        {
            endpointA.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(host.CurrentWorld.Tick + 1, -1, 0)));
            host.StepOnce();
            SnapshotV2 snapB = ReadLastSnapshotV2(endpointB);
            endpointB.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, snapB.SnapshotSeq)));
            if (snapB.Leaves.Contains(aId))
            {
                sawLeave = true;
                Assert.DoesNotContain(snapB.Enters, e => e.EntityId == aId);
                Assert.Equal(snapB.Leaves.OrderBy(id => id), snapB.Leaves);
                break;
            }
        }

        Assert.True(sawLeave);
        Assert.NotEqual(aId, bId);
    }

    [Fact]
    public void AOI_Budget_RespectsMaxEntities()
    {
        const int maxEntities = 10;
        ServerConfig cfg = ServerConfig.Default(9103) with
        {
            SnapshotEveryTicks = 1,
            MaxEntitiesPerSnapshot = maxEntities,
            VisionRadius = Fix32.FromInt(100),
            VisionRadiusSq = Fix32.FromInt(100) * Fix32.FromInt(100)
        };

        ServerHost host = CreateHostWithDenseZone(cfg, totalEntitiesInZone: 51);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("dense", "dense")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));
        host.AdvanceTicks(2);

        (EnterZoneAck ack, SnapshotV2 snapshot) = ReadEnterAckAndLastSnapshotV2(endpoint);
        Assert.True(snapshot.Entities.Length <= maxEntities);

        SnapshotEntity self = snapshot.Entities.Single(e => e.EntityId == ack.EntityId);
        Assert.Contains(snapshot.Entities, e => e.EntityId == self.EntityId);

        ZoneState zone = host.CurrentWorld.Zones.Single(z => z.Id.Value == 1);
        HashSet<int> expectedIds = zone.Entities
            .Select(entity => new
            {
                EntityId = entity.Id.Value,
                IsSelf = entity.Id.Value == ack.EntityId,
                DistanceSq = DistanceSq(entity.Pos.X.Raw, entity.Pos.Y.Raw, self.PosXRaw, self.PosYRaw)
            })
            .OrderByDescending(candidate => candidate.IsSelf)
            .ThenBy(candidate => candidate.DistanceSq)
            .ThenBy(candidate => candidate.EntityId)
            .Take(maxEntities)
            .Select(candidate => candidate.EntityId)
            .ToHashSet();

        int[] actualIds = snapshot.Entities.Select(entity => entity.EntityId).ToArray();
        Assert.Equal(actualIds.OrderBy(id => id), actualIds);
        Assert.Equal(expectedIds.OrderBy(id => id), actualIds);
    }

    [Fact]
    public void ReplayVerify_MultiZone_AOI_Passes()
    {
        DoDRunConfig cfg = new(Seed: 9104, ZoneCount: 2, BotCount: 8, TickCount: 300);
        DoDRunResult run1 = DoDRunner.Run(cfg);
        DoDRunResult run2 = DoDRunner.Run(cfg);

        Assert.Equal(TestChecksum.NormalizeFullHex(run1.Checksum), TestChecksum.NormalizeFullHex(run2.Checksum));
    }

    private static string CaptureSnapshotPayloadHash(int seed)
    {
        ServerHost host = new(ServerConfig.Default(seed) with
        {
            SnapshotEveryTicks = 1,
            VisionRadius = Fix32.FromInt(4),
            VisionRadiusSq = Fix32.FromInt(16),
            MaxEntitiesPerSnapshot = 64
        });

        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("stable", $"stable-{seed}")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(1)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(1, 0)));

        List<byte[]> snapshots = new();
        for (int i = 0; i < 12; i++)
        {
            host.StepOnce();
            while (endpoint.TryDequeueFromServer(out byte[] payload))
            {
                if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _) && message is SnapshotV2 snapshot)
                {
                    snapshots.Add(payload);
                    endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq)));
                    Assert.Equal(snapshot.Entities.Select(e => e.EntityId).OrderBy(id => id), snapshot.Entities.Select(e => e.EntityId));
                }
            }
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] payload in snapshots)
        {
            hash.AppendData(payload);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static ServerHost CreateHostWithDenseZone(ServerConfig cfg, int totalEntitiesInZone)
    {
        ServerHost baseline = new(cfg);
        WorldState world = baseline.CurrentWorld;
        ZoneState zone = world.Zones.Single(z => z.Id.Value == 1);

        List<(int X, int Y)> openTiles = CollectOpenTiles(zone.Map, totalEntitiesInZone);
        List<EntityState> entities = new();

        for (int i = 1; i <= totalEntitiesInZone; i++)
        {
            (int tileX, int tileY) = openTiles[i - 1];
            entities.Add(new EntityState(
                new EntityId(i),
                new Vec2Fix(Fix32.FromInt(tileX), Fix32.FromInt(tileY)),
                new Vec2Fix(Fix32.Zero, Fix32.Zero),
                100,
                100,
                true,
                Fix32.One,
                1,
                1,
                0,
                EntityKind.Npc));
        }

        ZoneState updatedZone = zone.WithEntities(entities.OrderByDescending(e => e.Id.Value).ToImmutableArray());
        WorldState updatedWorld = world with
        {
            Zones = world.Zones.Select(z => z.Id.Value == 1 ? updatedZone : z).ToImmutableArray(),
            EntityLocations = entities.Select(e => new EntityLocation(e.Id, new ZoneId(1))).OrderBy(e => e.Id.Value).ToImmutableArray()
        };

        return new ServerHost(cfg, bootstrap: new ServerBootstrap(updatedWorld, cfg.Seed, ImmutableArray<BootstrapPlayerRecord>.Empty));
    }

    private static (EnterZoneAck Ack, SnapshotV2 Snapshot) ReadEnterAckAndLastSnapshotV2(InMemoryEndpoint endpoint)
    {
        EnterZoneAck? ack = null;
        SnapshotV2? snapshot = null;

        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (!ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _))
            {
                continue;
            }

            if (message is EnterZoneAck typedAck)
            {
                ack = typedAck;
            }
            else if (message is SnapshotV2 typedSnapshot)
            {
                snapshot = typedSnapshot;
            }
        }

        Assert.NotNull(ack);
        Assert.NotNull(snapshot);
        return (ack!, snapshot!);
    }

    private static List<(int X, int Y)> CollectOpenTiles(TileMap map, int required)
    {
        List<(int X, int Y)> openTiles = new(required);
        for (int y = 0; y < map.Height && openTiles.Count < required; y++)
        {
            for (int x = 0; x < map.Width && openTiles.Count < required; x++)
            {
                if (map.Get(x, y) == TileKind.Empty)
                {
                    openTiles.Add((x, y));
                }
            }
        }

        Assert.True(openTiles.Count >= required, $"Not enough open tiles: required={required} available={openTiles.Count}");
        return openTiles;
    }

    private static long DistanceSq(int xRaw, int yRaw, int selfXRaw, int selfYRaw)
    {
        long dx = (long)xRaw - selfXRaw;
        long dy = (long)yRaw - selfYRaw;
        return (dx * dx) + (dy * dy);
    }

    private static SnapshotV2 ReadLastSnapshotV2(InMemoryEndpoint endpoint)
    {
        SnapshotV2? snapshot = null;
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _) && message is SnapshotV2 typed)
            {
                snapshot = typed;
            }
        }

        Assert.NotNull(snapshot);
        return snapshot!;
    }
}
