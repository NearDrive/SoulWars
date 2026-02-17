using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct TickReportCountInt(string Key, int Value);

public readonly record struct TickReportCountLong(string Key, long Value);

public sealed record TickReport(
    int Tick,
    string WorldChecksum,
    string? SnapshotHash,
    ImmutableArray<TickReportCountInt> EntityCountByType,
    int LootCount,
    ImmutableArray<TickReportCountInt> InventoryTotals,
    ImmutableArray<TickReportCountLong> WalletTotals);

public static class TickReportBuilder
{
    private const string GoldCurrencyId = "gold";

    public static TickReport Build(WorldState world, string worldChecksum, string? snapshotHash = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldChecksum);

        Dictionary<string, int> entityCountByType = new(StringComparer.Ordinal);
        foreach (ZoneState zone in world.Zones)
        {
            foreach (EntityState entity in zone.Entities)
            {
                string key = entity.Kind.ToString();
                entityCountByType.TryGetValue(key, out int current);
                entityCountByType[key] = current + 1;
            }
        }

        Dictionary<string, int> inventoryTotals = new(StringComparer.Ordinal);
        foreach (PlayerInventoryState playerInventory in world.PlayerInventories.IsDefault
                     ? ImmutableArray<PlayerInventoryState>.Empty
                     : world.PlayerInventories)
        {
            foreach (InventorySlot slot in playerInventory.Inventory.Slots)
            {
                if (slot.Quantity <= 0 || string.IsNullOrWhiteSpace(slot.ItemId))
                {
                    continue;
                }

                inventoryTotals.TryGetValue(slot.ItemId, out int current);
                inventoryTotals[slot.ItemId] = current + slot.Quantity;
            }
        }

        Dictionary<string, long> walletTotals = new(StringComparer.Ordinal);
        foreach (PlayerWalletState wallet in world.PlayerWallets.IsDefault
                     ? ImmutableArray<PlayerWalletState>.Empty
                     : world.PlayerWallets)
        {
            walletTotals.TryGetValue(GoldCurrencyId, out long current);
            walletTotals[GoldCurrencyId] = current + wallet.Gold;
        }

        return new TickReport(
            Tick: world.Tick,
            WorldChecksum: worldChecksum,
            SnapshotHash: snapshotHash,
            EntityCountByType: entityCountByType
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new TickReportCountInt(pair.Key, pair.Value))
                .ToImmutableArray(),
            LootCount: world.LootEntities.IsDefault ? 0 : world.LootEntities.Length,
            InventoryTotals: inventoryTotals
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new TickReportCountInt(pair.Key, pair.Value))
                .ToImmutableArray(),
            WalletTotals: walletTotals
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new TickReportCountLong(pair.Key, pair.Value))
                .ToImmutableArray());
    }
}
