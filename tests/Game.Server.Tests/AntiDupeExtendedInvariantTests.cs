using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Game.Protocol;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "Persistence")]
public sealed class AntiDupeExtendedInvariantTests
{
    [Fact]
    public void SaveLoadContinue_SameChecksum()
    {
        const int seed = 42042;
        const int splitTick = 120;
        const int totalTicks = 240;

        ServerConfig config = ServerConfig.Default(seed) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 2,
            NpcCountPerZone = 3
        };

        ServerHost baseline = new(config);
        baseline.AdvanceTicks(totalTicks);
        string baselineChecksum = TestChecksum.NormalizeFullHex(StateChecksum.Compute(baseline.CurrentWorld));
        CoreInvariants.Validate(baseline.CurrentWorld, baseline.CurrentWorld.Tick);

        ServerHost first = new(config);
        first.AdvanceTicks(splitTick);
        CoreInvariants.Validate(first.CurrentWorld, first.CurrentWorld.Tick);

        byte[] snapshotBytes = WorldStateSerializer.SaveToBytes(first.CurrentWorld);
        ServerBootstrap bootstrap = new(
            WorldStateSerializer.LoadFromBytes(snapshotBytes),
            config.Seed,
            ImmutableArray<BootstrapPlayerRecord>.Empty);

        ServerHost resumed = new(config, bootstrap: bootstrap);
        resumed.AdvanceTicks(totalTicks - splitTick);

        string resumedChecksum = TestChecksum.NormalizeFullHex(StateChecksum.Compute(resumed.CurrentWorld));
        Assert.Equal(baselineChecksum, resumedChecksum);
        CoreInvariants.Validate(resumed.CurrentWorld, resumed.CurrentWorld.Tick);
    }

    [Fact]
    public void KillLootRestart_Consistent()
    {
        ServerConfig config = ServerConfig.Default(seed: 42) with
        {
            ZoneCount = 1,
            MapWidth = 8,
            MapHeight = 8,
            NpcCountPerZone = 0,
            SnapshotEveryTicks = 1
        };

        WorldState world = BuildWorldWithNpc();
        ServerHost host = new(config, bootstrap: new ServerBootstrap(world, config.Seed, ImmutableArray<BootstrapPlayerRecord>.Empty));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "anti-dupe")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.StepOnce();

        _ = DequeueRequired<Welcome>(endpoint);
        EnterZoneAck ack = DequeueRequired<EnterZoneAck>(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new AttackIntent(1, ack.EntityId, 2, 1)));
        host.StepOnce();

        LootEntityState loot = Assert.Single(host.CurrentWorld.LootEntities);
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new LootIntent(loot.Id.Value, 1)));
        host.StepOnce();

        Assert.Empty(host.CurrentWorld.LootEntities);
        PlayerInventoryState beforeRestartInventory = Assert.Single(host.CurrentWorld.PlayerInventories);

        string dbPath = Path.Combine(Path.GetTempPath(), $"soulwars-pr42-{Guid.NewGuid():N}.db");
        try
        {
            host.SaveToSqlite(dbPath);
            ServerHost restarted = ServerHost.LoadFromSqlite(config, dbPath);

            CoreInvariants.Validate(restarted.CurrentWorld, restarted.CurrentWorld.Tick);

            int zoneEntityCount = restarted.CurrentWorld.Zones.SelectMany(z => z.Entities).Count();
            int distinctZoneEntityCount = restarted.CurrentWorld.Zones.SelectMany(z => z.Entities).Select(e => e.Id.Value).Distinct().Count();
            Assert.Equal(zoneEntityCount, distinctZoneEntityCount);

            Assert.Empty(restarted.CurrentWorld.LootEntities);

            PlayerInventoryState restartedInventory = Assert.Single(restarted.CurrentWorld.PlayerInventories);
            Assert.Equal(beforeRestartInventory.EntityId, restartedInventory.EntityId);
            Assert.Equal(SumInventoryItems(beforeRestartInventory.Inventory), SumInventoryItems(restartedInventory.Inventory));
            Assert.Equal(SumWorldItems(host.CurrentWorld), SumWorldItems(restarted.CurrentWorld));
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
    public void FuzzLootIntents_NoCrash()
    {
        ServerConfig config = ServerConfig.Default(seed: 77) with
        {
            ZoneCount = 2,
            MapWidth = 8,
            MapHeight = 8,
            NpcCountPerZone = 0,
            SnapshotEveryTicks = 1
        };

        WorldState world = BuildWorldWithNpc();
        ServerHost host = new(config, bootstrap: new ServerBootstrap(world, config.Seed, ImmutableArray<BootstrapPlayerRecord>.Empty));
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HelloV2("v", "fuzz-loot")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.StepOnce();

        _ = DequeueRequired<Welcome>(endpoint);
        EnterZoneAck ack = DequeueRequired<EnterZoneAck>(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new AttackIntent(1, ack.EntityId, 2, 1)));
        host.StepOnce();

        int realLootId = Assert.Single(host.CurrentWorld.LootEntities).Id.Value;

        (int LootId, int ZoneId)[] intents =
        {
            (int.MinValue, 1),
            (int.MaxValue, 1),
            (realLootId, 2),
            (realLootId, 1),
            (realLootId, 1),
            (realLootId, 1),
            (-999_999, 1),
            (0, 1),
            (realLootId + 1234, 1),
        };

        Exception? ex = Record.Exception(() =>
        {
            for (int tick = 0; tick < 120; tick++)
            {
                (int lootId, int zoneId) intent = intents[tick % intents.Length];
                endpoint.EnqueueToServer(ProtocolCodec.Encode(new LootIntent(intent.lootId, intent.zoneId)));
                host.StepOnce();
                CoreInvariants.Validate(host.CurrentWorld, host.CurrentWorld.Tick);
            }
        });

        Assert.Null(ex);
    }

    private static Dictionary<string, int> SumInventoryItems(InventoryComponent inventory)
        => inventory.Slots
            .Where(s => s.Quantity > 0)
            .GroupBy(s => s.ItemId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity), StringComparer.Ordinal);

    private static Dictionary<string, int> SumWorldItems(WorldState world)
    {
        Dictionary<string, int> totals = new(StringComparer.Ordinal);

        foreach (PlayerInventoryState inventoryState in (world.PlayerInventories.IsDefault ? ImmutableArray<PlayerInventoryState>.Empty : world.PlayerInventories))
        {
            foreach (InventorySlot slot in inventoryState.Inventory.Slots)
            {
                if (slot.Quantity <= 0)
                {
                    continue;
                }

                totals[slot.ItemId] = totals.TryGetValue(slot.ItemId, out int value) ? value + slot.Quantity : slot.Quantity;
            }
        }

        foreach (LootEntityState loot in (world.LootEntities.IsDefault ? ImmutableArray<LootEntityState>.Empty : world.LootEntities))
        {
            foreach (ItemStack item in loot.Items)
            {
                totals[item.ItemId] = totals.TryGetValue(item.ItemId, out int value) ? value + item.Quantity : item.Quantity;
            }
        }

        return totals;
    }

    private static WorldState BuildWorldWithNpc()
    {
        TileMap map = BuildOpenMap(8, 8);
        Vec2Fix npcPos = new(Fix32.FromInt(1) + new Fix32(Fix32.OneRaw / 2), Fix32.FromInt(1) + new Fix32(Fix32.OneRaw / 2));

        EntityState npc = new(
            Id: new EntityId(2),
            Pos: npcPos,
            Vel: Vec2Fix.Zero,
            MaxHp: 10,
            Hp: 10,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: -10,
            Kind: EntityKind.Npc,
            NextWanderChangeTick: int.MaxValue,
            WanderX: 0,
            WanderY: 0);

        ZoneState zone1 = new(new ZoneId(1), map, ImmutableArray.Create(npc));
        ZoneState zone2 = new(new ZoneId(2), map, ImmutableArray<EntityState>.Empty);
        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone1, zone2),
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
