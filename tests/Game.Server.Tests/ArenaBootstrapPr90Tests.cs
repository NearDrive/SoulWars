using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "PR90")]
public sealed class ArenaBootstrapPr90Tests
{
    [Fact]
    [Trait("Category", "PR90")]
    public void ArenaFixture_IsDeterministicAcrossRuns()
    {
        SimulationConfig config = SimulationConfig.Default(9001) with
        {
            MapWidth = 32,
            MapHeight = 32,
            ZoneCount = 1,
            NpcCountPerZone = 0
        };

        WorldState first = ArenaZoneFactory.CreateWorld(config);
        WorldState second = ArenaZoneFactory.CreateWorld(config);

        ZoneState firstZone = Assert.Single(first.Zones);
        ZoneState secondZone = Assert.Single(second.Zones);

        var firstEntities = firstZone.Entities
            .Select(entity => (Id: entity.Id.Value, entity.Kind, X: entity.Pos.X.Raw, Y: entity.Pos.Y.Raw))
            .ToArray();
        var secondEntities = secondZone.Entities
            .Select(entity => (Id: entity.Id.Value, entity.Kind, X: entity.Pos.X.Raw, Y: entity.Pos.Y.Raw))
            .ToArray();

        Assert.Equal(firstEntities.Select(e => e.Id).OrderBy(id => id), firstEntities.Select(e => e.Id));
        Assert.Equal(secondEntities.Select(e => e.Id).OrderBy(id => id), secondEntities.Select(e => e.Id));
        Assert.Equal(firstEntities, secondEntities);
        Assert.Equal(StateChecksum.Compute(first), StateChecksum.Compute(second));
    }

    [Fact]
    [Trait("Category", "PR90")]
    public void ArenaFixture_RespectsFogAndAOI()
    {
        ServerConfig config = ServerConfig.Default(9002) with
        {
            SnapshotEveryTicks = 1,
            ArenaMode = true,
            VisionRadius = Fix32.FromInt(6),
            VisionRadiusSq = Fix32.FromInt(6) * Fix32.FromInt(6)
        };

        ServerHost host = new(config);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("pr90", "arena-observer")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(ArenaZoneFactory.ArenaZoneId)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new ClientAckV2(ArenaZoneFactory.ArenaZoneId, 0)));

        host.AdvanceTicks(2);

        EnterZoneAck ack = ReadFirst<EnterZoneAck>(endpoint);
        SnapshotV2 snapshot = ReadLastSnapshot(endpoint);

        ZoneState zone = host.CurrentWorld.Zones.Single(z => z.Id.Value == ArenaZoneFactory.ArenaZoneId);
        EntityState self = zone.Entities.Single(e => e.Id.Value == ack.EntityId);
        VisibilityAoiProvider aoi = new(config.AoiRadiusSq);

        HashSet<int> expectedVisibleIds = aoi.ComputeVisible(zone, self)
            .Select(entity => entity.Id.Value)
            .ToHashSet();
        HashSet<int> actualSnapshotIds = snapshot.Entities
            .Select(entity => entity.EntityId)
            .ToHashSet();

        Assert.Equal(expectedVisibleIds.OrderBy(id => id), actualSnapshotIds.OrderBy(id => id));

        foreach (EntityState hidden in zone.Entities.Where(entity => !expectedVisibleIds.Contains(entity.Id.Value)))
        {
            Assert.DoesNotContain(snapshot.Entities, entity => entity.EntityId == hidden.Id.Value);
        }
    }

    private static T ReadFirst<T>(InMemoryEndpoint endpoint)
        where T : class, IServerMessage
    {
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            IServerMessage decoded = ProtocolCodec.DecodeServer(payload);
            if (decoded is T typed)
            {
                return typed;
            }
        }

        throw new Xunit.Sdk.XunitException($"Message {typeof(T).Name} not found.");
    }

    private static SnapshotV2 ReadLastSnapshot(InMemoryEndpoint endpoint)
    {
        SnapshotV2? latest = null;
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            IServerMessage decoded = ProtocolCodec.DecodeServer(payload);
            if (decoded is SnapshotV2 snapshot)
            {
                latest = snapshot;
            }
        }

        return latest ?? throw new Xunit.Sdk.XunitException("SnapshotV2 not found.");
    }
}
