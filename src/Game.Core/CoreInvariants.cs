namespace Game.Core;

public static class CoreInvariants
{
    private const int MinAcceptedLastAttackTick = -1_000_000;

    public static void Validate(WorldState world, int tick)
    {
        HashSet<int> seenEntityIds = new();
        Dictionary<int, int> entityZones = new();
        int lastZoneId = int.MinValue;

        foreach (ZoneState zone in world.Zones)
        {
            if (zone.Id.Value <= lastZoneId)
            {
                throw new InvariantViolationException($"invariant=ZonesOrdered tick={tick} zoneId={zone.Id.Value} lastZoneId={lastZoneId}");
            }

            lastZoneId = zone.Id.Value;

            if (zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Masks.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Kinds.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Positions.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Health.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Combat.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Ai.Length)
            {
                throw new InvariantViolationException($"invariant=ParallelArraysLength tick={tick} zoneId={zone.Id.Value}");
            }

            int lastAliveId = int.MinValue;
            for (int i = 0; i < zone.EntitiesData.AliveIds.Length; i++)
            {
                int id = zone.EntitiesData.AliveIds[i].Value;
                if (id <= lastAliveId)
                {
                    throw new InvariantViolationException($"invariant=AliveIdsOrdered tick={tick} zoneId={zone.Id.Value} entityId={id} lastEntityId={lastAliveId}");
                }

                lastAliveId = id;
                ComponentMask mask = zone.EntitiesData.Masks[i];
                if (!mask.Has(ComponentMask.PositionBit)
                    || !mask.Has(ComponentMask.HealthBit)
                    || !mask.Has(ComponentMask.CombatBit))
                {
                    throw new InvariantViolationException($"invariant=RequiredMaskBits tick={tick} zoneId={zone.Id.Value} entityId={id} mask={mask.Bits}");
                }

                bool expectsAi = zone.EntitiesData.Kinds[i] == EntityKind.Npc;
                bool hasAi = mask.Has(ComponentMask.AiBit);
                if (expectsAi != hasAi)
                {
                    throw new InvariantViolationException($"invariant=AiMaskMatchesKind tick={tick} zoneId={zone.Id.Value} entityId={id} kind={zone.EntitiesData.Kinds[i]} hasAi={hasAi}");
                }
            }

            int lastEntityId = int.MinValue;
            foreach (EntityState entity in zone.Entities)
            {
                if (entity.Id.Value <= lastEntityId)
                {
                    throw new InvariantViolationException($"invariant=EntitiesOrdered tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} lastEntityId={lastEntityId}");
                }

                lastEntityId = entity.Id.Value;

                if (!seenEntityIds.Add(entity.Id.Value))
                {
                    throw new InvariantViolationException($"invariant=UniqueEntityAcrossZones tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value}");
                }

                entityZones[entity.Id.Value] = zone.Id.Value;

                if (entity.Hp > entity.MaxHp)
                {
                    throw new InvariantViolationException($"invariant=HealthUpperBound tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} hp={entity.Hp} maxHp={entity.MaxHp}");
                }

                if (entity.Hp < 0)
                {
                    throw new InvariantViolationException($"invariant=HealthLowerBound tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} hp={entity.Hp}");
                }

                if (entity.IsAlive != (entity.Hp > 0))
                {
                    throw new InvariantViolationException($"invariant=IsAliveMatchesHp tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} hp={entity.Hp} isAlive={entity.IsAlive}");
                }

                if (entity.Hp <= 0)
                {
                    throw new InvariantViolationException($"invariant=NoDeadEntityInZone tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} hp={entity.Hp}");
                }

                if (entity.AttackCooldownTicks < 0)
                {
                    throw new InvariantViolationException($"invariant=AttackCooldownNonNegative tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} cooldown={entity.AttackCooldownTicks}");
                }

                if (entity.LastAttackTick > tick)
                {
                    throw new InvariantViolationException($"invariant=LastAttackNotInFuture tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} lastAttackTick={entity.LastAttackTick}");
                }

                if (entity.LastAttackTick < MinAcceptedLastAttackTick)
                {
                    throw new InvariantViolationException($"invariant=LastAttackTickLowerBound tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} lastAttackTick={entity.LastAttackTick}");
                }

                if (entity.AttackDamage < 0)
                {
                    throw new InvariantViolationException($"invariant=AttackDamageNonNegative tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} damage={entity.AttackDamage}");
                }

                if (entity.AttackRange.Raw < 0)
                {
                    throw new InvariantViolationException($"invariant=AttackRangeNonNegative tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} attackRangeRaw={entity.AttackRange.Raw}");
                }

                int tileX = Fix32.FloorToInt(entity.Pos.X);
                int tileY = Fix32.FloorToInt(entity.Pos.Y);
                if (zone.Map.Get(tileX, tileY) == TileKind.Solid)
                {
                    throw new InvariantViolationException($"invariant=EntityNotOnSolidTile tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} tileX={tileX} tileY={tileY}");
                }
            }
        }

        int lastLocationEntityId = int.MinValue;
        foreach (EntityLocation location in world.EntityLocations)
        {
            if (location.Id.Value <= lastLocationEntityId)
            {
                throw new InvariantViolationException($"invariant=EntityLocationsOrdered tick={tick} entityId={location.Id.Value} lastEntityId={lastLocationEntityId}");
            }

            lastLocationEntityId = location.Id.Value;

            if (!entityZones.TryGetValue(location.Id.Value, out int zoneId) || zoneId != location.ZoneId.Value)
            {
                throw new InvariantViolationException($"invariant=EntityLocationMatchesZone tick={tick} entityId={location.Id.Value} locationZoneId={location.ZoneId.Value}");
            }
        }

        if (entityZones.Count != world.EntityLocations.Length)
        {
            throw new InvariantViolationException($"invariant=EntityLocationCountMatches tick={tick} zoneEntities={entityZones.Count} locations={world.EntityLocations.Length}");
        }
    }
}
