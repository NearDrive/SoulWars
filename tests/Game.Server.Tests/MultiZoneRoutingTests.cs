using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class MultiZoneRoutingTests
{
    [Fact]
    public void EnterLeave_Repeated_DoesNotDuplicateEntity()
    {
        ServerHost host = new(ServerConfig.Default(seed: 100) with { SnapshotEveryTicks = 1, ZoneCount = 3 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "alice")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(2)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(2)));

        host.AdvanceTicks(2);

        IServerMessage[] initialMessages = DrainMessages(endpoint);
        Welcome welcome = Assert.IsType<Welcome>(initialMessages.First(message => message is Welcome));
        EnterZoneAck[] enterAcks = initialMessages.OfType<EnterZoneAck>().ToArray();
        Assert.True(enterAcks.Length >= 1);

        PlayerId playerId = welcome.PlayerId;
        Assert.True(host.TryGetPlayerState(playerId, out PlayerState state));
        Assert.NotNull(state.EntityId);
        Assert.Equal(1, host.CountWorldEntitiesForPlayer(playerId));

        int entityId = state.EntityId!.Value;

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new LeaveZoneRequestV2(2)));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(2)));
        host.AdvanceTicks(2);

        Assert.True(host.TryGetPlayerState(playerId, out state));
        Assert.Equal(entityId, state.EntityId);
        Assert.Equal(1, host.CountWorldEntitiesForPlayer(playerId));
    }

    [Fact]
    public void Session_WithoutZone_DoesNotGenerateSnapshot()
    {
        ServerHost host = new(ServerConfig.Default(seed: 101) with { SnapshotEveryTicks = 1 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "no-zone")));
        host.AdvanceTicks(5);

        IServerMessage[] messages = DrainMessages(endpoint);
        Assert.Contains(messages, m => m is Welcome);
        Assert.DoesNotContain(messages, m => m is Snapshot or SnapshotV2);
    }

    [Fact]
    public void MultiZone_3Zones_IndependentBots_ChecksumStable_TwoRuns()
    {
        string checksumRun1 = RunMultiZoneScenario(seed: 2025);
        string checksumRun2 = RunMultiZoneScenario(seed: 2025);

        Assert.Equal(checksumRun1, checksumRun2);
    }

    private static string RunMultiZoneScenario(int seed)
    {
        ServerConfig cfg = ServerConfig.Default(seed) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 3,
            NpcCountPerZone = 0
        };

        ServerHost host = new(cfg);
        List<InMemoryEndpoint> endpoints = new();
        List<(InMemoryEndpoint Endpoint, int ZoneId)> clients = new();

        for (int i = 0; i < 9; i++)
        {
            int zoneId = (i % 3) + 1;
            InMemoryEndpoint endpoint = new();
            endpoints.Add(endpoint);
            host.Connect(endpoint);
            endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("bot", $"bot-{i:000}")));
            endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequestV2(zoneId)));
            clients.Add((endpoint, zoneId));
        }

        host.AdvanceTicks(2);
        foreach (InMemoryEndpoint endpoint in endpoints)
        {
            _ = DrainMessages(endpoint);
        }

        for (int tick = 1; tick <= 200; tick++)
        {
            for (int i = 0; i < clients.Count; i++)
            {
                sbyte moveX = (sbyte)(((tick + i) % 3) - 1);
                sbyte moveY = (sbyte)(((tick + (i * 2)) % 3) - 1);
                clients[i].Endpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(tick, moveX, moveY)));
            }

            host.StepOnce();
            foreach (InMemoryEndpoint endpoint in endpoints)
            {
                _ = DrainMessages(endpoint);
            }
        }

        EnsureEntityNotDuplicatedAcrossZones(host.CurrentWorld);
        return StateChecksum.Compute(host.CurrentWorld);
    }

    private static void EnsureEntityNotDuplicatedAcrossZones(WorldState world)
    {
        HashSet<int> entityIds = new();
        foreach (ZoneState zone in world.Zones)
        {
            foreach (EntityState entity in zone.Entities)
            {
                Assert.True(entityIds.Add(entity.Id.Value), $"Entity {entity.Id.Value} duplicated across zones.");
            }
        }
    }

    private static IServerMessage[] DrainMessages(InMemoryEndpoint endpoint)
    {
        List<IServerMessage> result = new();
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _) && message is not null)
            {
                result.Add(message);
            }
        }

        return result.ToArray();
    }
}
