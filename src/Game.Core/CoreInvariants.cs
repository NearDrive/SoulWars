namespace Game.Core;

public static class CoreInvariants
{
    public static void Validate(WorldState world)
    {
        HashSet<int> seenEntityIds = new();
        Dictionary<int, int> entityZones = new();
        int lastZoneId = int.MinValue;
        foreach (ZoneState zone in world.Zones)
        {
            if (zone.Id.Value <= lastZoneId)
            {
                throw new InvariantViolationException($"Zones are not strictly ordered by ZoneId. zoneId={zone.Id.Value} last={lastZoneId}");
            }

            lastZoneId = zone.Id.Value;

            int lastEntityId = int.MinValue;
            foreach (EntityState entity in zone.Entities)
            {
                if (entity.Id.Value <= lastEntityId)
                {
                    throw new InvariantViolationException($"Entities are not strictly ordered by EntityId in zone {zone.Id.Value}. entityId={entity.Id.Value} last={lastEntityId}");
                }

                lastEntityId = entity.Id.Value;

                if (!seenEntityIds.Add(entity.Id.Value))
                {
                    throw new InvariantViolationException($"Entity {entity.Id.Value} exists in multiple zones.");
                }

                entityZones[entity.Id.Value] = zone.Id.Value;

                if (entity.Hp > entity.MaxHp)
                {
                    throw new InvariantViolationException($"Entity {entity.Id.Value} has Hp above MaxHp in zone {zone.Id.Value}.");
                }

                if (entity.Hp < 0)
                {
                    throw new InvariantViolationException($"Entity {entity.Id.Value} has negative Hp in zone {zone.Id.Value}.");
                }

                if (entity.IsAlive != (entity.Hp > 0))
                {
                    throw new InvariantViolationException($"Entity {entity.Id.Value} has inconsistent IsAlive/Hp in zone {zone.Id.Value}.");
                }

                if (entity.Hp <= 0)
                {
                    throw new InvariantViolationException($"Entity {entity.Id.Value} with Hp <= 0 present in zone {zone.Id.Value}.");
                }

                int tileX = Fix32.FloorToInt(entity.Pos.X);
                int tileY = Fix32.FloorToInt(entity.Pos.Y);
                if (zone.Map.Get(tileX, tileY) == TileKind.Solid)
                {
                    throw new InvariantViolationException($"Entity {entity.Id.Value} overlaps solid tile at ({tileX},{tileY}) in zone {zone.Id.Value}.");
                }
            }
        }

        int lastLocationEntityId = int.MinValue;
        foreach (EntityLocation location in world.EntityLocations.OrderBy(l => l.Id.Value))
        {
            if (location.Id.Value <= lastLocationEntityId)
            {
                throw new InvariantViolationException("EntityLocations are not strictly ordered by EntityId.");
            }

            lastLocationEntityId = location.Id.Value;

            if (!entityZones.TryGetValue(location.Id.Value, out int zoneId) || zoneId != location.ZoneId.Value)
            {
                throw new InvariantViolationException($"EntityLocation mismatch for entity {location.Id.Value}.");
            }
        }

        if (entityZones.Count != world.EntityLocations.Length)
        {
            throw new InvariantViolationException("EntityLocations count mismatch.");
        }
    }
}
