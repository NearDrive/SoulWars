using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

public sealed class PlayerDeathPenaltyTests
{
    [Fact]
    public void PlayerDeath_DropsFullInventory_AndClearsInventory()
    {
        SimulationConfig config = CreateConfig(42);
        WorldState state = BuildCombatWorldWithInventories();

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(1), new ZoneId(1), TargetEntityId: new EntityId(2)))));

        LootEntityState loot = Assert.Single(state.LootEntities);
        Assert.Equal(DerivePlayerDeathLootEntityId(new EntityId(2)), loot.Id);
        Assert.Equal(new ZoneId(1), loot.ZoneId);

        Assert.Collection(loot.Items,
            item => { Assert.Equal("gold.coin", item.ItemId); Assert.Equal(50, item.Quantity); },
            item => { Assert.Equal("ore.iron", item.ItemId); Assert.Equal(20, item.Quantity); },
            item => { Assert.Equal("potion.minor", item.ItemId); Assert.Equal(3, item.Quantity); });

        PlayerInventoryState deadPlayerInventory = state.PlayerInventories.Single(i => i.EntityId.Value == 2);
        Assert.All(deadPlayerInventory.Inventory.Slots, slot => Assert.Equal(0, slot.Quantity));

        PlayerDeathAuditEntry deathAudit = Assert.Single(state.PlayerDeathAuditLog);
        Assert.Equal(state.Tick, deathAudit.Tick);
        Assert.Equal(new EntityId(2), deathAudit.PlayerEntityId);
        Assert.Equal(loot.Id, deathAudit.LootEntityId);
    }

    [Fact]
    public void OtherBotsCanLoot_PlayerDeathDrop()
    {
        SimulationConfig config = CreateConfig(88);
        WorldState state = BuildCombatWorldWithInventories();

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(1), new ZoneId(1), TargetEntityId: new EntityId(2)))));

        EntityId deathLootId = Assert.Single(state.LootEntities).Id;

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.LootIntent, new EntityId(3), new ZoneId(1), LootEntityId: deathLootId))));

        Assert.Empty(state.LootEntities);
        PlayerInventoryState botInventory = state.PlayerInventories.Single(i => i.EntityId.Value == 3);
        Assert.Collection(botInventory.Inventory.Slots.Where(s => s.Quantity > 0),
            slot => { Assert.Equal("gold.coin", slot.ItemId); Assert.Equal(50, slot.Quantity); },
            slot => { Assert.Equal("ore.iron", slot.ItemId); Assert.Equal(20, slot.Quantity); },
            slot => { Assert.Equal("potion.minor", slot.ItemId); Assert.Equal(3, slot.Quantity); });

        string checksumA = RunDeathThenLootSequenceChecksum(config);
        string checksumB = RunDeathThenLootSequenceChecksum(config);
        Assert.Equal(checksumA, checksumB);
    }

    [Fact]
    public void NoDuplication_OnReconnectOrRepeatDeathHandling()
    {
        SimulationConfig config = CreateConfig(99);
        WorldState preDeath = BuildCombatWorldWithInventories();
        Inputs deathInputs = new(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(1), new ZoneId(1), TargetEntityId: new EntityId(2))));

        WorldState afterDeath = Simulation.Step(config, preDeath, deathInputs);
        Assert.Single(afterDeath.LootEntities);

        WorldState loadedAfterDeath = WorldStateSerializer.LoadFromBytes(WorldStateSerializer.SaveToBytes(afterDeath));
        WorldState afterReloadTick = Simulation.Step(config, loadedAfterDeath, new Inputs(ImmutableArray<WorldCommand>.Empty));

        Assert.Single(afterReloadTick.LootEntities);
        int worldLootItemSum = afterReloadTick.LootEntities.SelectMany(l => l.Items).Sum(i => i.Quantity);
        int inventoryItemSum = afterReloadTick.PlayerInventories.Sum(i => i.Inventory.Slots.Sum(s => s.Quantity));
        Assert.Equal(73, worldLootItemSum + inventoryItemSum);

        WorldState replayed = Simulation.Step(config, preDeath, deathInputs);
        Assert.Single(replayed.LootEntities);
        Assert.Equal(StateChecksum.Compute(afterDeath), StateChecksum.Compute(replayed));
    }

    private static string RunDeathThenLootSequenceChecksum(SimulationConfig config)
    {
        WorldState state = BuildCombatWorldWithInventories();
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(1), new ZoneId(1), TargetEntityId: new EntityId(2)))));

        EntityId lootId = Assert.Single(state.LootEntities).Id;

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.LootIntent, new EntityId(3), new ZoneId(1), LootEntityId: lootId))));

        return StateChecksum.Compute(state);
    }

    private static WorldState BuildCombatWorldWithInventories()
    {
        TileMap map = BuildOpenMap(8, 8);

        EntityState killer = CreatePlayer(new EntityId(1), new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)), attackDamage: 100);
        EntityState victim = CreatePlayer(new EntityId(2), new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2) + new Fix32(Fix32.OneRaw / 2)), attackDamage: 1);
        EntityState looterBot = CreatePlayer(new EntityId(3), new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2) + new Fix32(Fix32.OneRaw / 4)), attackDamage: 1);

        ZoneState zone = new(new ZoneId(1), map, ImmutableArray.Create(killer, victim, looterBot));

        InventoryComponent victimInventory = InventoryComponent.CreateDefault() with
        {
            Slots = CreateVictimSlots().ToImmutableArray()
        };

        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: ImmutableArray.Create(
                new EntityLocation(new EntityId(1), new ZoneId(1)),
                new EntityLocation(new EntityId(2), new ZoneId(1)),
                new EntityLocation(new EntityId(3), new ZoneId(1))),
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            PlayerInventories: ImmutableArray.Create(
                new PlayerInventoryState(new EntityId(2), victimInventory)));
    }

    private static IEnumerable<InventorySlot> CreateVictimSlots()
    {
        for (int i = 0; i < InventoryConstants.DefaultCapacity; i++)
        {
            if (i == 0)
            {
                yield return new InventorySlot("gold.coin", 50);
            }
            else if (i == 2)
            {
                yield return new InventorySlot("potion.minor", 3);
            }
            else if (i == 5)
            {
                yield return new InventorySlot("ore.iron", 20);
            }
            else
            {
                yield return new InventorySlot(string.Empty, 0);
            }
        }
    }

    private static EntityState CreatePlayer(EntityId id, Vec2Fix pos, int attackDamage)
        => new(
            Id: id,
            Pos: pos,
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: attackDamage,
            AttackCooldownTicks: 10,
            LastAttackTick: -10,
            Kind: EntityKind.Player);

    private static EntityId DerivePlayerDeathLootEntityId(EntityId deadPlayerId)
        => new(unchecked(int.MinValue + deadPlayerId.Value));

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
