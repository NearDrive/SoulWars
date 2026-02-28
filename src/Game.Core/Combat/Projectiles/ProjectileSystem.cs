using System.Collections.Immutable;

namespace Game.Core;

public static class ProjectileSystem
{
    public static bool IsProjectileSkill(SkillDefinition skill) => skill.UsesProjectile || skill.ProjectileSpeedRaw > 0;

    public static (ZoneState Zone, ImmutableArray<ProjectileEvent> Events, int NextProjectileId, uint DroppedSpawns) SpawnProjectiles(
        SimulationConfig config,
        int tick,
        ZoneState zone,
        ImmutableArray<SkillCastIntent> intents,
        int nextProjectileId)
    {
        if (intents.IsDefaultOrEmpty)
        {
            return (zone, ImmutableArray<ProjectileEvent>.Empty, nextProjectileId, 0);
        }

        ImmutableArray<ProjectileComponent> currentProjectiles = zone.Projectiles.IsDefault ? ImmutableArray<ProjectileComponent>.Empty : zone.Projectiles;
        List<ProjectileComponent> mutable = currentProjectiles.OrderBy(p => p.ProjectileId).ToList();
        ImmutableArray<ProjectileEvent>.Builder events = ImmutableArray.CreateBuilder<ProjectileEvent>();
        uint dropped = 0;

        foreach (SkillCastIntent intent in intents.OrderBy(i => i.CasterId.Value).ThenBy(i => i.SkillId.Value).ThenBy(i => i.TargetEntityId.Value))
        {
            SkillDefinition? skill = FindSkill(config, intent.SkillId);
            if (skill is null || !IsProjectileSkill(skill.Value))
            {
                continue;
            }

            if (config.MaxProjectilesPerZone > 0 && mutable.Count >= config.MaxProjectilesPerZone)
            {
                dropped++;
                continue;
            }

            int ownerIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, intent.CasterId);
            if (ownerIndex < 0)
            {
                continue;
            }

            EntityState owner = zone.Entities[ownerIndex];
            Vec2Fix targetPos = ResolveTargetPosition(zone, intent);
            Fix32 speed = new Fix32(skill.Value.ProjectileSpeedRaw);
            Fix32 velX = Fix32.Clamp(targetPos.X - owner.Pos.X, -speed, speed);
            Fix32 velY = Fix32.Clamp(targetPos.Y - owner.Pos.Y, -speed, speed);
            if (velX.Raw == 0 && velY.Raw == 0)
            {
                velX = speed;
            }

            ProjectileComponent projectile = new(
                ProjectileId: nextProjectileId++,
                OwnerId: intent.CasterId,
                TargetId: intent.TargetEntityId,
                VelX: velX,
                VelY: velY,
                Radius: new Fix32(skill.Value.HitRadiusRaw),
                TargetX: targetPos.X,
                TargetY: targetPos.Y,
                PosX: owner.Pos.X,
                PosY: owner.Pos.Y,
                SkillId: intent.SkillId,
                SpawnTick: tick,
                MaxLifetimeTicks: Math.Max(1, config.MaxProjectileLifetimeTicks),
                CollidesWithWorld: skill.Value.CollidesWithWorld,
                RequiresLoSOnSpawn: (skill.Value.Flags & SkillFlags.RequiresLineOfSight) != 0);

            mutable.Add(projectile);
            events.Add(new ProjectileEvent(tick, projectile.ProjectileId, ProjectileEventKind.Spawn, projectile.OwnerId, projectile.TargetId, projectile.SkillId, projectile.PosX, projectile.PosY));
        }

        ImmutableArray<ProjectileComponent> orderedProjectiles = mutable.OrderBy(p => p.ProjectileId).ToImmutableArray();
        return (zone.WithProjectiles(orderedProjectiles), events.ToImmutable(), nextProjectileId, dropped);
    }

    public static (ZoneState Zone, ImmutableArray<CombatEvent> CombatEvents, ImmutableArray<CombatLogEvent> CombatLogEvents, ImmutableArray<ProjectileEvent> ProjectileEvents) StepProjectiles(
        SimulationConfig config,
        int tick,
        ZoneState zone)
    {
        if (zone.Projectiles.IsDefaultOrEmpty)
        {
            return (zone, ImmutableArray<CombatEvent>.Empty, ImmutableArray<CombatLogEvent>.Empty, ImmutableArray<ProjectileEvent>.Empty);
        }

        ImmutableArray<ProjectileComponent>.Builder kept = ImmutableArray.CreateBuilder<ProjectileComponent>();
        ImmutableArray<ProjectileEvent>.Builder projectileEvents = ImmutableArray.CreateBuilder<ProjectileEvent>();

        foreach (ProjectileComponent projectile in zone.Projectiles.OrderBy(p => p.ProjectileId))
        {
            if (tick - projectile.SpawnTick >= projectile.MaxLifetimeTicks)
            {
                projectileEvents.Add(new ProjectileEvent(tick, projectile.ProjectileId, ProjectileEventKind.Despawn, projectile.OwnerId, projectile.TargetId, projectile.SkillId, projectile.PosX, projectile.PosY));
                continue;
            }

            Fix32 nextX = projectile.PosX + projectile.VelX;
            Fix32 nextY = projectile.PosY + projectile.VelY;

            if (projectile.CollidesWithWorld)
            {
                int tileX = Fix32.FloorToInt(nextX);
                int tileY = Fix32.FloorToInt(nextY);
                if (zone.Map.Get(tileX, tileY) == TileKind.Solid)
                {
                    projectileEvents.Add(new ProjectileEvent(tick, projectile.ProjectileId, ProjectileEventKind.Despawn, projectile.OwnerId, projectile.TargetId, projectile.SkillId, nextX, nextY));
                    continue;
                }
            }

            EntityId? firstHit = ResolveFirstHitTarget(zone, projectile, nextX, nextY);
            if (firstHit is EntityId targetId)
            {
                projectileEvents.Add(new ProjectileEvent(tick, projectile.ProjectileId, ProjectileEventKind.Hit, projectile.OwnerId, targetId, projectile.SkillId, nextX, nextY));
                continue;
            }

            kept.Add(projectile with { PosX = nextX, PosY = nextY });
        }

        ZoneState updatedZone = zone.WithProjectiles(kept.ToImmutable().OrderBy(p => p.ProjectileId).ToImmutableArray());
        return (updatedZone, ImmutableArray<CombatEvent>.Empty, ImmutableArray<CombatLogEvent>.Empty, projectileEvents.ToImmutable());
    }

    private static EntityId? ResolveFirstHitTarget(ZoneState zone, ProjectileComponent projectile, Fix32 nextX, Fix32 nextY)
    {
        Fix32 radiusSq = projectile.Radius * projectile.Radius;
        if (radiusSq.Raw <= 0)
        {
            return null;
        }

        EntityId? best = null;
        foreach (EntityState entity in zone.Entities.OrderBy(e => e.Id.Value))
        {
            if (!entity.IsAlive || entity.Id.Value == projectile.OwnerId.Value)
            {
                continue;
            }

            Fix32 dx = entity.Pos.X - nextX;
            Fix32 dy = entity.Pos.Y - nextY;
            Fix32 distSq = (dx * dx) + (dy * dy);
            if (distSq > radiusSq)
            {
                continue;
            }

            best = entity.Id;
            break;
        }

        return best;
    }

    private static Vec2Fix ResolveTargetPosition(ZoneState zone, SkillCastIntent intent)
    {
        if ((CastTargetKind)intent.TargetType == CastTargetKind.Point)
        {
            return new Vec2Fix(intent.TargetX, intent.TargetY);
        }

        int index = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, intent.TargetEntityId);
        if (index < 0)
        {
            return new Vec2Fix(intent.TargetX, intent.TargetY);
        }

        return zone.Entities[index].Pos;
    }

    private static SkillDefinition? FindSkill(SimulationConfig config, SkillId skillId)
    {
        ImmutableArray<SkillDefinition> skills = config.SkillDefinitions.IsDefault ? ImmutableArray<SkillDefinition>.Empty : config.SkillDefinitions;
        for (int i = 0; i < skills.Length; i++)
        {
            if (skills[i].Id.Value == skillId.Value)
            {
                return skills[i];
            }
        }

        return null;
    }
}
