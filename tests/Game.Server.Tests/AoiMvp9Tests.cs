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

        SnapshotV2 initialA = ReadLastSnapshotV2(endpointA);
        SnapshotV2 initialB = ReadLastSnapshotV2(endpointB);
        int aId = initialA.Entities.Single(e => e.Kind == SnapshotEntityKind.Player).EntityId;
        int bId = initialB.Entities.Single(e => e.Kind == SnapshotEntityKind.Player).EntityId;

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

        SnapshotV2 snapshot = ReadLastSnapshotV2(endpoint);
        Assert.True(snapshot.Entities.Length <= maxEntities);

        SnapshotEntity self = snapshot.Entities.Single(e => e.Kind == SnapshotEntityKind.Player);
        Assert.Contains(snapshot.Entities, e => e.EntityId == self.EntityId);

        long[] distances = snapshot.Entities
            .Select(entity => DistanceSq(entity, self))
            .ToArray();

        Assert.True(distances.SequenceEqual(distances.OrderBy(d => d)), "Selected entities must be nearest first (after canonical id order this must remain monotonic in this setup).");

        SnapshotEntity[] byDistanceThenId = snapshot.Entities
            .OrderBy(entity => DistanceSq(entity, self))
            .ThenBy(entity => entity.EntityId)
            .ToArray();
        Assert.Equal(byDistanceThenId.Select(e => e.EntityId), snapshot.Entities.Select(e => e.EntityId));
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

        List<EntityState> entities = new();
        entities.Add(new EntityState(new EntityId(1), new Vec2Fix(Fix32.Zero, Fix32.Zero), new Vec2Fix(Fix32.Zero, Fix32.Zero), 100, 100, true, Fix32.One, 1, 1, 0, EntityKind.Player));

        for (int i = 2; i <= totalEntitiesInZone; i++)
        {
            int ring = (i - 2) / 8;
            int offset = (i - 2) % 8;
            int x = ring + 1;
            int y = offset;
            entities.Add(new EntityState(new EntityId(i), new Vec2Fix(Fix32.FromInt(x), Fix32.FromInt(y)), new Vec2Fix(Fix32.Zero, Fix32.Zero), 10, 10, true, Fix32.One, 1, 1, 0, EntityKind.Npc));
        }

        ZoneState updatedZone = zone.WithEntities(entities.OrderByDescending(e => e.Id.Value).ToImmutableArray());
        WorldState updatedWorld = world with
        {
            Zones = world.Zones.Select(z => z.Id.Value == 1 ? updatedZone : z).ToImmutableArray(),
            EntityLocations = entities.Select(e => new EntityLocation(e.Id, new ZoneId(1))).OrderBy(e => e.Id.Value).ToImmutableArray()
        };

        return new ServerHost(cfg, bootstrap: new ServerBootstrap(updatedWorld, cfg.Seed, ImmutableArray<BootstrapPlayerRecord>.Empty));
    }

    private static long DistanceSq(SnapshotEntity entity, SnapshotEntity self)
    {
        long dx = (long)entity.PosXRaw - self.PosXRaw;
        long dy = (long)entity.PosYRaw - self.PosYRaw;
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
