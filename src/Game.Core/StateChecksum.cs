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

        ImmutableArray<EntityLocation> orderedLocations = state.EntityLocations
            .OrderBy(location => location.Id.Value)
            .ToImmutableArray();
        writer.Write(orderedLocations.Length);
        foreach (EntityLocation location in orderedLocations)
        {
            writer.Write(location.Id.Value);
            writer.Write(location.ZoneId.Value);
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
