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

            if (zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Masks.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Kinds.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Positions.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Health.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Combat.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Ai.Length)
            {
                throw new InvariantViolationException($"Zone {zone.Id.Value} has inconsistent component array lengths.");
            }

            int lastAliveId = int.MinValue;
            for (int i = 0; i < zone.EntitiesData.AliveIds.Length; i++)
            {
                int id = zone.EntitiesData.AliveIds[i].Value;
                if (id <= lastAliveId)
                {
                    throw new InvariantViolationException($"Zone {zone.Id.Value} AliveIds are not strictly sorted.");
                }

                lastAliveId = id;
                ComponentMask mask = zone.EntitiesData.Masks[i];
                if (!mask.Has(ComponentMask.PositionBit)
                    || !mask.Has(ComponentMask.HealthBit)
                    || !mask.Has(ComponentMask.CombatBit))
                {
                    throw new InvariantViolationException($"Entity {id} has invalid required component mask bits.");
                }

                bool expectsAi = zone.EntitiesData.Kinds[i] == EntityKind.Npc;
                bool hasAi = mask.Has(ComponentMask.AiBit);
                if (expectsAi != hasAi)
                {
                    throw new InvariantViolationException($"Entity {id} has inconsistent AI mask for kind {zone.EntitiesData.Kinds[i]}.");
                }
            }

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
