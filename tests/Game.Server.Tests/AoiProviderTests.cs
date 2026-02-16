using System.Collections.Immutable;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class AoiProviderTests
{
    [Fact]
    public void Aoi_RadiusBoundary_IsDeterministic()
    {
        EntityId viewerId = new(10);
        EntityId onRadiusId = new(20);
        EntityId outRadiusId = new(30);

        Fix32 radius = Fix32.FromInt(2);
        RadiusAoiProvider provider = new(radius * radius);
        WorldState world = BuildWorld(
            new EntityState(viewerId, new Vec2Fix(Fix32.Zero, Fix32.Zero), Vec2Fix.Zero, 10, 10, true, Fix32.One, 1, 1, 0),
            new EntityState(onRadiusId, new Vec2Fix(Fix32.FromInt(2), Fix32.Zero), Vec2Fix.Zero, 10, 10, true, Fix32.One, 1, 1, 0),
            new EntityState(outRadiusId, new Vec2Fix(new Fix32(Fix32.FromInt(2).Raw + 1), Fix32.Zero), Vec2Fix.Zero, 10, 10, true, Fix32.One, 1, 1, 0));

        int[]? baseline = null;
        for (int i = 0; i < 10; i++)
        {
            VisibleSet visible = provider.ComputeVisible(world, new ZoneId(1), viewerId);
            int[] ids = visible.EntityIds.Select(id => id.Value).ToArray();

            Assert.Contains(viewerId.Value, ids);
            Assert.Contains(onRadiusId.Value, ids);
            Assert.DoesNotContain(outRadiusId.Value, ids);

            baseline ??= ids;
            Assert.Equal(baseline, ids);
        }
    }

    [Fact]
    public void Aoi_Result_IsSortedByEntityId()
    {
        RadiusAoiProvider provider = new(Fix32.FromInt(100));
        WorldState world = BuildWorld(
            new EntityState(new EntityId(50), new Vec2Fix(Fix32.Zero, Fix32.Zero), Vec2Fix.Zero, 10, 10, true, Fix32.One, 1, 1, 0),
            new EntityState(new EntityId(5), new Vec2Fix(Fix32.One, Fix32.Zero), Vec2Fix.Zero, 10, 10, true, Fix32.One, 1, 1, 0),
            new EntityState(new EntityId(22), new Vec2Fix(Fix32.Zero, Fix32.One), Vec2Fix.Zero, 10, 10, true, Fix32.One, 1, 1, 0));

        VisibleSet visible = provider.ComputeVisible(world, new ZoneId(1), new EntityId(50));
        int[] ids = visible.EntityIds.Select(id => id.Value).ToArray();

        Assert.Equal(new[] { 5, 22, 50 }, ids);
    }

    [Fact]
    public void Aoi_SameSeed_SameVisibleSet_TwoRuns()
    {
        int[] run1 = CaptureVisibleAtTick200(seed: 999);
        int[] run2 = CaptureVisibleAtTick200(seed: 999);

        Assert.Equal(run1, run2);
    }

    [Fact]
    public void Snapshot_PartialAoi_Golden()
    {
        ServerHost host = CreateGoldenHost();
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("viewer", "viewer")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));

        host.StepOnce();

        Snapshot snapshot = ReadLastSnapshot(endpoint);
        string actualJson = SerializeSnapshot(snapshot);
        string expectedJson = File.ReadAllText(ResolveFixturePath("snapshot_partial_aoi_golden.json")).Trim();

        Assert.Equal(new[] { 1, 2 }, snapshot.Entities.Select(e => e.EntityId).OrderBy(id => id));
        Assert.Equal(expectedJson, actualJson);
    }

    private static int[] CaptureVisibleAtTick200(int seed)
    {
        Fix32 visionRadius = Fix32.FromInt(3);
        ServerHost host = new(ServerConfig.Default(seed) with
        {
            SnapshotEveryTicks = 1,
            VisionRadius = visionRadius,
            VisionRadiusSq = visionRadius * visionRadius
        });

        InMemoryEndpoint endpoint0 = new();
        InMemoryEndpoint endpoint1 = new();

        host.Connect(endpoint0);
        host.Connect(endpoint1);

        endpoint0.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("bot-0", "bot-0")));
        endpoint1.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("bot-1", "bot-1")));
        endpoint0.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        endpoint1.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));

        for (int i = 0; i < 200; i++)
        {
            host.StepOnce();
        }

        Snapshot snapshot = ReadLastSnapshot(endpoint0);
        return snapshot.Entities.Select(entity => entity.EntityId).ToArray();
    }

    private static ServerHost CreateGoldenHost()
    {
        ImmutableArray<EntityState> entities = ImmutableArray.Create(
            new EntityState(new EntityId(1), new Vec2Fix(Fix32.Zero, Fix32.Zero), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0),
            new EntityState(new EntityId(2), new Vec2Fix(Fix32.FromInt(2), Fix32.Zero), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0),
            new EntityState(new EntityId(3), new Vec2Fix(Fix32.FromInt(5), Fix32.Zero), Vec2Fix.Zero, 100, 100, true, Fix32.One, 1, 1, 0));

        ZoneState zone = new(new ZoneId(1), new TileMap(8, 8, Enumerable.Repeat(TileKind.Empty, 64).ToImmutableArray()), entities);
        WorldState world = new(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: ImmutableArray.Create(
                new EntityLocation(new EntityId(1), new ZoneId(1)),
                new EntityLocation(new EntityId(2), new ZoneId(1)),
                new EntityLocation(new EntityId(3), new ZoneId(1))));

        ServerBootstrap bootstrap = new(
            world,
            ServerSeed: 321,
            Players: ImmutableArray.Create(new BootstrapPlayerRecord("viewer", 100, 1, 1)));

        Fix32 visionRadius = Fix32.FromInt(2);
        ServerConfig config = ServerConfig.Default(seed: 321) with
        {
            SnapshotEveryTicks = 1,
            VisionRadius = visionRadius,
            VisionRadiusSq = visionRadius * visionRadius,
            NpcCountPerZone = 0
        };

        return new ServerHost(config, bootstrap: bootstrap);
    }

    private static WorldState BuildWorld(params EntityState[] entities)
    {
        ImmutableArray<EntityState> allEntities = entities.ToImmutableArray();
        ZoneState zone = new(new ZoneId(1), new TileMap(4, 4, Enumerable.Repeat(TileKind.Empty, 16).ToImmutableArray()), allEntities);

        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: allEntities.Select(e => new EntityLocation(e.Id, new ZoneId(1))).ToImmutableArray());
    }


    private static string SerializeSnapshot(Snapshot snapshot)
    {
        string entities = string.Join(
            ",",
            snapshot.Entities
                .OrderBy(entity => entity.EntityId)
                .Select(entity =>
                    $"{{\"entityId\":{entity.EntityId},\"posXRaw\":{entity.PosXRaw},\"posYRaw\":{entity.PosYRaw},\"velXRaw\":{entity.VelXRaw},\"velYRaw\":{entity.VelYRaw},\"hp\":{entity.Hp},\"kind\":\"{entity.Kind}\"}}"));

        return $"{{\"tick\":{snapshot.Tick},\"zoneId\":{snapshot.ZoneId},\"entities\":[{entities}]}}";
    }

    private static string ResolveFixturePath(string fileName)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string solutionPath = Path.Combine(dir.FullName, "Game.sln");
            if (File.Exists(solutionPath))
            {
                string fixturePath = Path.Combine(dir.FullName, "tests", "Fixtures", fileName);
                if (!File.Exists(fixturePath))
                {
                    throw new FileNotFoundException($"Fixture not found at '{fixturePath}'.", fixturePath);
                }

                return fixturePath;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not resolve repository root from '{AppContext.BaseDirectory}'.");
    }

    private static Snapshot ReadLastSnapshot(InMemoryEndpoint endpoint)
    {
        Snapshot? snapshot = null;

        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _) && message is Snapshot typed)
            {
                snapshot = typed;
            }
        }

        return Assert.IsType<Snapshot>(snapshot);
    }
}
