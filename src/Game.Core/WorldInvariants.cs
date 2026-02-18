using System.Collections.Immutable;

namespace Game.Core;

public static class WorldInvariants
{
    public static void AssertNoEntityDupesAcrossZones(WorldState world, int tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        Dictionary<int, int> firstZoneByEntityId = new();

        foreach (ZoneState zone in world.Zones.OrderBy(z => z.Id.Value))
        {
            foreach (EntityState entity in zone.Entities.OrderBy(e => e.Id.Value))
            {
                if (firstZoneByEntityId.TryGetValue(entity.Id.Value, out int firstZoneId))
                {
                    throw new InvariantViolationException(
                        $"invariant=UniqueEntityAcrossZones tick={tick} entityId={entity.Id.Value} firstZoneId={firstZoneId} secondZoneId={zone.Id.Value}");
                }

                firstZoneByEntityId[entity.Id.Value] = zone.Id.Value;
            }
        }
    }


    public static void AssertNoCrossZoneDupes(WorldState world, int tick)
        => AssertNoEntityDupesAcrossZones(world, tick);

    public static void AssertSortedAscending<T>(
        IReadOnlyList<T> values,
        Func<T, int> keySelector,
        string arrayName,
        int tick,
        int? zoneId = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(arrayName);

        for (int i = 1; i < values.Count; i++)
        {
            int prev = keySelector(values[i - 1]);
            int curr = keySelector(values[i]);
            if (curr < prev)
            {
                string zoneSuffix = zoneId is null ? string.Empty : $" zoneId={zoneId.Value}";
                throw new InvariantViolationException(
                    $"invariant=CanonicalOrder array={arrayName} tick={tick}{zoneSuffix} index={i} prev={prev} curr={curr}");
            }
        }
    }
}
