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
        writer.Write(state.Map.Width);
        writer.Write(state.Map.Height);

        byte[] mapHash = ComputeMapHash(state.Map);
        writer.Write(mapHash.Length);
        writer.Write(mapHash);

        ImmutableArray<EntityState> orderedEntities = state.Entities
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
