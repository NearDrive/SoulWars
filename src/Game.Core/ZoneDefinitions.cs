using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Game.Core;

public sealed record ZoneAabb(Vec2Fix Center, Vec2Fix HalfExtents);

public sealed record ZoneBounds(Fix32 MinX, Fix32 MinY, Fix32 MaxX, Fix32 MaxY)
{
    public bool Contains(Vec2Fix point)
        => point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
}

public sealed record NpcSpawnDefinition(
    string NpcArchetypeId,
    int Count,
    int Level,
    ImmutableArray<Vec2Fix> SpawnPoints);

public sealed record LootRulesDefinition;

public sealed record ZoneDefinition(
    ZoneId ZoneId,
    ZoneBounds Bounds,
    ImmutableArray<ZoneAabb> StaticObstacles,
    ImmutableArray<NpcSpawnDefinition> NpcSpawns,
    LootRulesDefinition? LootRules,
    Vec2Fix? RespawnPoint = null);

public sealed record ZoneDefinitions(ImmutableArray<ZoneDefinition> Zones)
{
    public bool TryGetZone(ZoneId zoneId, out ZoneDefinition definition)
    {
        foreach (ZoneDefinition zone in Zones)
        {
            if (zone.ZoneId.Value == zoneId.Value)
            {
                definition = zone;
                return true;
            }
        }

        definition = null!;
        return false;
    }
}

public static class ZoneDefinitionCanonicalizer
{
    public static string CanonicalizeAndHash(ZoneDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        StringBuilder builder = new();

        foreach (ZoneDefinition zone in definitions.Zones.OrderBy(z => z.ZoneId.Value))
        {
            builder.Append("zone|").Append(zone.ZoneId.Value).Append('\n');
            builder
                .Append("bounds|")
                .Append(zone.Bounds.MinX.Raw).Append('|')
                .Append(zone.Bounds.MinY.Raw).Append('|')
                .Append(zone.Bounds.MaxX.Raw).Append('|')
                .Append(zone.Bounds.MaxY.Raw)
                .Append('\n');

            foreach (ZoneAabb obstacle in zone.StaticObstacles
                         .OrderBy(o => o.Center.X.Raw)
                         .ThenBy(o => o.Center.Y.Raw)
                         .ThenBy(o => o.HalfExtents.X.Raw)
                         .ThenBy(o => o.HalfExtents.Y.Raw))
            {
                builder
                    .Append("obstacle|")
                    .Append(obstacle.Center.X.Raw).Append('|')
                    .Append(obstacle.Center.Y.Raw).Append('|')
                    .Append(obstacle.HalfExtents.X.Raw).Append('|')
                    .Append(obstacle.HalfExtents.Y.Raw)
                    .Append('\n');
            }

            if (zone.RespawnPoint is not null)
            {
                builder
                    .Append("respawn|")
                    .Append(zone.RespawnPoint.Value.X.Raw).Append('|')
                    .Append(zone.RespawnPoint.Value.Y.Raw)
                    .Append('\n');
            }

            foreach (NpcSpawnDefinition spawn in zone.NpcSpawns
                         .Select((value, index) => (Spawn: value, Index: index))
                         .OrderBy(x => x.Spawn.NpcArchetypeId, StringComparer.Ordinal)
                         .ThenBy(x => x.Index)
                         .Select(x => x.Spawn))
            {
                builder
                    .Append("spawn|")
                    .Append(spawn.NpcArchetypeId).Append('|')
                    .Append(spawn.Level).Append('|')
                    .Append(spawn.Count)
                    .Append('\n');

                foreach (Vec2Fix point in spawn.SpawnPoints
                             .OrderBy(p => p.X.Raw)
                             .ThenBy(p => p.Y.Raw))
                {
                    builder
                        .Append("point|")
                        .Append(point.X.Raw).Append('|')
                        .Append(point.Y.Raw)
                        .Append('\n');
                }
            }
        }

        byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
