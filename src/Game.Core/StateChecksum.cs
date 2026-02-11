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

        ImmutableArray<EntityState> orderedEntities = state.Entities
            .OrderBy(entity => entity.Id.Value)
            .ToImmutableArray();

        writer.Write(orderedEntities.Length);

        foreach (EntityState entity in orderedEntities)
        {
            writer.Write(entity.Id.Value);
            writer.Write(entity.X);
            writer.Write(entity.Y);
        }

        writer.Flush();
        byte[] hash = SHA256.HashData(stream.ToArray());

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
