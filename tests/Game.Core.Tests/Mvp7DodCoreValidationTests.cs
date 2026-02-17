using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "MVP7")]
public sealed class Mvp7DodCoreValidationTests
{
    [Fact]
    public void Mvp7Dod_Loot_IsDeterministic()
    {
        LootSimulationTests loot = new();
        loot.SameCombat_SameLoot();
        loot.ReplayChecksum_Stable_WithLoot();
    }

    [Fact]
    public void Mvp7Dod_Inventory_Authoritative_SaveLoad()
    {
        InventorySimulationTests inventory = new();
        inventory.Loot_ToInventory_CorrectStacksAndCapacity();
        inventory.Restart_InventoryIntact();
    }

    [Fact]
    public void Mvp7Dod_PlayerDeath_DropsInventory_NoDupe()
    {
        PlayerDeathPenaltyTests death = new();
        death.PlayerDeath_DropsFullInventory_AndClearsInventory();
        death.NoDuplication_OnReconnectOrRepeatDeathHandling();
    }

    [Fact]
    public void Mvp7Dod_Vendor_BuySell_Atomic_SaveLoad()
    {
        VendorSimulationTests vendor = new();
        vendor.VendorBuy_Deterministic();
        vendor.RestartConsistent_VendorAndWallet();
    }
}
