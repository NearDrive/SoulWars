using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

public sealed class VendorSimulationTests
{
    [Fact]
    public void VendorBuy_Deterministic()
    {
        SimulationConfig config = CreateConfig(123);
        WorldState state = BuildWorld();

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(
                Kind: WorldCommandKind.VendorBuyIntent,
                EntityId: new EntityId(1),
                ZoneId: new ZoneId(1),
                VendorId: "vendor.safe.1",
                ItemId: "potion.minor",
                Quantity: 3))));

        PlayerWalletState wallet = Assert.Single(state.PlayerWallets);
        Assert.Equal(Simulation.DefaultStartingGold - 75, wallet.Gold);

        PlayerInventoryState inv = Assert.Single(state.PlayerInventories);
        Assert.Equal("potion.minor", inv.Inventory.Slots[0].ItemId);
        Assert.Equal(3, inv.Inventory.Slots[0].Quantity);

        VendorTransactionAuditEntry audit = Assert.Single(state.VendorTransactionAuditLog);
        Assert.Equal(1, audit.Tick);
        Assert.Equal(VendorAction.Buy, audit.Action);
        Assert.Equal(25, audit.UnitPrice);
        Assert.Equal(Simulation.DefaultStartingGold, audit.GoldBefore);
        Assert.Equal(Simulation.DefaultStartingGold - 75, audit.GoldAfter);
    }

    [Fact]
    public void RestartConsistent_VendorAndWallet()
    {
        SimulationConfig config = CreateConfig(456);
        WorldState state = BuildWorld();

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.VendorBuyIntent, new EntityId(1), new ZoneId(1), VendorId: "vendor.safe.1", ItemId: "potion.minor", Quantity: 4))));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.VendorSellIntent, new EntityId(1), new ZoneId(1), VendorId: "vendor.safe.1", ItemId: "potion.minor", Quantity: 2))));

        byte[] snap = WorldStateSerializer.SaveToBytes(state);
        WorldState loaded = WorldStateSerializer.LoadFromBytes(snap);

        Assert.Equal(state.PlayerWallets.Single().Gold, loaded.PlayerWallets.Single().Gold);
        Assert.Equal(state.PlayerInventories.Single().Inventory.Slots[0].Quantity, loaded.PlayerInventories.Single().Inventory.Slots[0].Quantity);
        Assert.Equal(state.Vendors.Single().VendorId, loaded.Vendors.Single().VendorId);
        CoreInvariants.Validate(loaded, loaded.Tick);
    }

    [Fact]
    public void ReplayIntact_WithVendor()
    {
        string a = RunScenario(999);
        string b = RunScenario(999);
        Assert.Equal(a, b);
    }

    private static string RunScenario(int seed)
    {
        SimulationConfig config = CreateConfig(seed);
        WorldState state = BuildWorld();
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.VendorBuyIntent, new EntityId(1), new ZoneId(1), VendorId: "vendor.safe.1", ItemId: "potion.minor", Quantity: 2))));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.VendorSellIntent, new EntityId(1), new ZoneId(1), VendorId: "vendor.safe.1", ItemId: "potion.minor", Quantity: 1))));
        return StateChecksum.Compute(state);
    }

    private static WorldState BuildWorld()
    {
        TileMap map = BuildOpenMap(8, 8);
        Vec2Fix pos = new(Fix32.FromInt(3), Fix32.FromInt(3));
        EntityState player = new(new EntityId(1), pos, Vec2Fix.Zero, 100, 100, true, Fix32.FromInt(1), 10, 10, -10, EntityKind.Player);
        ZoneState zone = new(new ZoneId(1), map, ImmutableArray.Create(player));
        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: ImmutableArray.Create(new EntityLocation(new EntityId(1), new ZoneId(1))),
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            PlayerInventories: ImmutableArray<PlayerInventoryState>.Empty,
            PlayerWallets: ImmutableArray<PlayerWalletState>.Empty,
            VendorTransactionAuditLog: ImmutableArray<VendorTransactionAuditEntry>.Empty,
            Vendors: ImmutableArray.Create(new VendorDefinition("vendor.safe.1", new ZoneId(1), ImmutableArray.Create(
                new VendorOfferDefinition("potion.minor", 25, 10, 10),
                new VendorOfferDefinition("gold.coin", 1, 1, 99)))));
    }

    private static TileMap BuildOpenMap(int width, int height)
    {
        ImmutableArray<TileKind>.Builder tiles = ImmutableArray.CreateBuilder<TileKind>(width * height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            tiles.Add(x == 0 || y == 0 || x == width - 1 || y == height - 1 ? TileKind.Solid : TileKind.Empty);
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
