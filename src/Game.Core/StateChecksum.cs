using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Game.Core;

public static class StateChecksum
{
    public static string Compute(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(state.Tick);

        ImmutableArray<ZoneState> orderedZones = state.Zones
            .OrderBy(zone => zone.Id.Value)
            .ToImmutableArray();

        writer.Write(orderedZones.Length);

        foreach (ZoneState zone in orderedZones)
        {
            writer.Write(zone.Id.Value);
            writer.Write(zone.Map.Width);
            writer.Write(zone.Map.Height);

            byte[] mapHash = ComputeMapHash(zone.Map);
            writer.Write(mapHash.Length);
            writer.Write(mapHash);

            ImmutableArray<EntityState> orderedEntities = zone.Entities
                .OrderBy(entity => entity.Id.Value)
                .ToImmutableArray();

            writer.Write(orderedEntities.Length);

            foreach (EntityState entity in orderedEntities)
            {
                writer.Write(entity.Id.Value);
                writer.Write(entity.Pos.X.Raw);
                writer.Write(entity.Pos.Y.Raw);
                writer.Write(entity.Vel.X.Raw);
                writer.Write(entity.Vel.Y.Raw);
            }
        }


        ImmutableArray<PlayerInventoryState> orderedInventories = (state.PlayerInventories.IsDefault ? ImmutableArray<PlayerInventoryState>.Empty : state.PlayerInventories)
            .OrderBy(i => i.EntityId.Value)
            .ToImmutableArray();
        if (!orderedInventories.IsDefaultOrEmpty)
        {
            writer.Write(orderedInventories.Length);

            foreach (PlayerInventoryState inv in orderedInventories)
            {
                writer.Write(inv.EntityId.Value);
                writer.Write(inv.Inventory.Capacity);
                writer.Write(inv.Inventory.StackLimit);
                writer.Write(inv.Inventory.Slots.Length);
                for (int i = 0; i < inv.Inventory.Slots.Length; i++)
                {
                    InventorySlot slot = inv.Inventory.Slots[i];
                    writer.Write(slot.ItemId ?? string.Empty);
                    writer.Write(slot.Quantity);
                }
            }
        }

        writer.Flush();
        byte[] hash = SHA256.HashData(stream.ToArray());

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] ComputeMapHash(TileMap map)
    {
        byte[] tileBytes = new byte[map.Tiles.Length];

        for (int i = 0; i < map.Tiles.Length; i++)
        {
            tileBytes[i] = (byte)map.Tiles[i];
        }

        return SHA256.HashData(tileBytes);
    }
}
