using System.Security.Cryptography;
using System.Text;
using Game.Core;
using Game.Persistence;

namespace Game.Persistence.Sqlite;

public sealed record SnapshotMeta(
    string SerializerVersion,
    string ZoneDefinitionsHash,
    string ConfigHash,
    string? BuildHash)
{
    public static SnapshotMeta Legacy { get; } = new(
        SerializerVersion: "legacy",
        ZoneDefinitionsHash: "legacy",
        ConfigHash: "legacy",
        BuildHash: null);
}

public static class SnapshotMetaBuilder
{
    public static SnapshotMeta Create(WorldState world, SimulationConfig simulationConfig, ZoneDefinitions? zoneDefinitions, string? buildHash = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        string serializerVersion = WorldStateSerializer.SerializerVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string zoneHash = zoneDefinitions is null
            ? ComputeWorldZoneHash(world)
            : ZoneDefinitionCanonicalizer.CanonicalizeAndHash(zoneDefinitions);
        string configHash = ComputeConfigHash(simulationConfig);

        return new SnapshotMeta(serializerVersion, zoneHash, configHash, buildHash);
    }

    private static string ComputeWorldZoneHash(WorldState world)
    {
        StringBuilder builder = new();

        foreach (ZoneState zone in world.Zones.OrderBy(z => z.Id.Value))
        {
            builder
                .Append("zone|")
                .Append(zone.Id.Value)
                .Append('|')
                .Append(zone.Map.Width)
                .Append('|')
                .Append(zone.Map.Height)
                .Append('\n');
        }

        return HashHex(builder.ToString());
    }

    private static string ComputeConfigHash(SimulationConfig config)
    {
        string canonical = string.Join("|", new[]
        {
            config.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.TickHz.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.DtFix.Raw.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.MoveSpeed.Raw.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.MaxSpeed.Raw.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.Radius.Raw.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.ZoneCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.MapWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.MapHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.NpcCountPerZone.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.NpcWanderPeriodTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.NpcAggroRange.Raw.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.Invariants.EnableCoreInvariants ? "1" : "0",
            config.Invariants.EnableServerInvariants ? "1" : "0"
        });

        return HashHex(canonical);
    }

    private static string HashHex(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
