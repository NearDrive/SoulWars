using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

public sealed class InventorySimulationTests
{
    [Fact]
    public void Loot_ToInventory_CorrectStacksAndCapacity()
    {
        SimulationConfig config = CreateConfig(1234);
        WorldState state = BuildWorldWithPlayerAndLoot(new[]
        {
            new ItemStack("gold.coin", 120),
            new ItemStack("potion.minor", 2)
        });

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(
                Kind: WorldCommandKind.LootIntent,
                EntityId: new EntityId(1),
                ZoneId: new ZoneId(1),
                LootEntityId: new EntityId(-10)))));

        Assert.Empty(state.LootEntities);
        PlayerInventoryState invState = Assert.Single(state.PlayerInventories);
        InventoryComponent inv = invState.Inventory;
        Assert.Equal(InventoryConstants.DefaultCapacity, inv.Capacity);
        Assert.Equal(99, inv.Slots[0].Quantity);
        Assert.Equal("gold.coin", inv.Slots[0].ItemId);
        Assert.Equal(21, inv.Slots[1].Quantity);
        Assert.Equal("gold.coin", inv.Slots[1].ItemId);
        Assert.Equal(2, inv.Slots[2].Quantity);
        Assert.Equal("potion.minor", inv.Slots[2].ItemId);
        Assert.All(inv.Slots, slot => Assert.InRange(slot.Quantity, 0, inv.StackLimit));

        WorldState failState = BuildWorldWithPlayerAndLoot(new[] { new ItemStack("ore", 1981) });
        failState = Simulation.Step(config, failState, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.LootIntent, new EntityId(1), new ZoneId(1), LootEntityId: new EntityId(-10)))));

        Assert.Single(failState.LootEntities);
        Assert.Empty(failState.PlayerInventories);
    }

    [Fact]
    public void Restart_InventoryIntact()
    {
        SimulationConfig config = CreateConfig(55);
        WorldState state = BuildWorldWithPlayerAndLoot(new[] { new ItemStack("gold.coin", 100), new ItemStack("potion.minor", 4) });
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.LootIntent, new EntityId(1), new ZoneId(1), LootEntityId: new EntityId(-10)))));

        byte[] data = WorldStateSerializer.SaveToBytes(state);
        WorldState loaded = WorldStateSerializer.LoadFromBytes(data);

        Assert.Equal(state.PlayerInventories, loaded.PlayerInventories);
        CoreInvariants.Validate(loaded, loaded.Tick);
    }

    [Fact]
    public void SaveLoad_ChecksumStable_WithInventory()
    {
        string runA = RunSequenceChecksum(77);
        string runB = RunSequenceChecksum(77);

        Assert.Equal(runA, runB);
    }

    private static string RunSequenceChecksum(int seed)
    {
        SimulationConfig config = CreateConfig(seed);
        WorldState state = BuildWorldWithPlayerAndLoot(new[] { new ItemStack("gold.coin", 7), new ItemStack("potion.minor", 1) });

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.LootIntent, new EntityId(1), new ZoneId(1), LootEntityId: new EntityId(-10)))));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        byte[] snap = WorldStateSerializer.SaveToBytes(state);
        state = WorldStateSerializer.LoadFromBytes(snap);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        return StateChecksum.Compute(state);
    }

    private static WorldState BuildWorldWithPlayerAndLoot(IEnumerable<ItemStack> lootItems)
    {
        TileMap map = BuildOpenMap(8, 8);
        Vec2Fix pos = new(Fix32.FromInt(3), Fix32.FromInt(3));
        EntityState player = new(
            Id: new EntityId(1),
            Pos: pos,
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: -10,
            Kind: EntityKind.Player);

        ZoneState zone = new(new ZoneId(1), map, ImmutableArray.Create(player));
        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: ImmutableArray.Create(new EntityLocation(new EntityId(1), new ZoneId(1))),
            LootEntities: ImmutableArray.Create(new LootEntityState(new EntityId(-10), new ZoneId(1), pos, lootItems.ToImmutableArray())),
            PlayerInventories: ImmutableArray<PlayerInventoryState>.Empty);
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

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: 1,
        MapWidth: 8,
        MapHeight: 8,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
