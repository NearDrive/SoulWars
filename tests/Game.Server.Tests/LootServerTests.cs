using System.Collections.Immutable;
using Game.Core;
using Game.Protocol;
using Xunit;

namespace Game.Server.Tests;

public sealed class LootServerTests
{
    [Fact]
    public void LootCollected_Despawns_NoDup_NoOrphan_OnRestart()
    {
        ServerConfig config = new()
        {
            Seed = 7,
            ZoneCount = 1,
            MapWidth = 8,
            MapHeight = 8,
            NpcCountPerZone = 0,
            SnapshotEveryTicks = 1
        };

        WorldState world = BuildWorldWithNpc();
        ServerBootstrap bootstrap = new(
            world,
            config.Seed,
            ImmutableArray<BootstrapPlayerRecord>.Empty);

        ServerHost host = new(config, bootstrap: bootstrap);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "alice")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.StepOnce();

        Welcome welcome = DequeueRequired<Welcome>(endpoint);
        EnterZoneAck ack = DequeueRequired<EnterZoneAck>(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new AttackIntent(1, ack.EntityId, 2, 1)));
        host.StepOnce();

        LootEntityState loot = Assert.Single(host.CurrentWorld.LootEntities);
        Assert.Equal(-2, loot.Id.Value);
        Assert.Equal(loot.Id.Value, host.CurrentWorld.LootEntities.Select(l => l.Id.Value).Distinct().Single());

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new LootIntent(loot.Id.Value, 1)));
        host.StepOnce();

        Assert.Empty(host.CurrentWorld.LootEntities);

        Assert.True(host.TryGetPlayerState(welcome.PlayerId, out PlayerState player));
        Assert.Equal(2, player.PendingLoot.Length);

        string dbPath = Path.Combine(Path.GetTempPath(), $"soulwars-loot-{Guid.NewGuid():N}.db");
        try
        {
            host.SaveToSqlite(dbPath);
            ServerHost restarted = ServerHost.LoadFromSqlite(config, dbPath);

            Assert.Empty(restarted.CurrentWorld.LootEntities);
            Assert.Equal(
                restarted.CurrentWorld.LootEntities.Length,
                restarted.CurrentWorld.LootEntities.Select(l => l.Id.Value).Distinct().Count());

            Assert.True(restarted.TryGetPlayerState(player.PlayerId, out PlayerState restartedPlayer));
            Assert.Empty(restartedPlayer.PendingLoot);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static WorldState BuildWorldWithNpc()
    {
        TileMap map = BuildOpenMap(8, 8);
        Vec2Fix npcPos = new(Fix32.FromInt(3), Fix32.FromInt(3));

        EntityState npc = new(
            Id: new EntityId(2),
            Pos: npcPos,
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: -10,
            Kind: EntityKind.Npc,
            NextWanderChangeTick: int.MaxValue,
            WanderX: 0,
            WanderY: 0);

        ZoneState zone = new(new ZoneId(1), map, ImmutableArray.Create(npc));
        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: ImmutableArray.Create(new EntityLocation(new EntityId(2), new ZoneId(1))),
            LootEntities: ImmutableArray<LootEntityState>.Empty);
    }

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

    private static T DequeueRequired<T>(InMemoryEndpoint endpoint) where T : class, IServerMessage
    {
        while (endpoint.TryDequeueToClient(out byte[] payload))
        {
            IServerMessage message = ProtocolCodec.DecodeServer(payload);
            if (message is T typed)
            {
                return typed;
            }
        }

        throw new Xunit.Sdk.XunitException($"Expected message {typeof(T).Name}.");
    }
}
