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
            ProjectileComponent projectile = new(
                ProjectileId: nextProjectileId++,
                OwnerId: intent.CasterId,
                TargetId: intent.TargetEntityId,
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
            events.Add(new ProjectileEvent(tick, projectile.ProjectileId, ProjectileEventKind.Spawn, projectile.OwnerId, projectile.TargetId, projectile.PosX, projectile.PosY));
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

        ImmutableArray<EntityState> entities = zone.Entities;
        Dictionary<int, EntityState> updates = new();
        ImmutableArray<ProjectileComponent>.Builder kept = ImmutableArray.CreateBuilder<ProjectileComponent>();
        ImmutableArray<CombatEvent>.Builder combatEvents = ImmutableArray.CreateBuilder<CombatEvent>();
        ImmutableArray<CombatLogEvent>.Builder combatLogEvents = ImmutableArray.CreateBuilder<CombatLogEvent>();
        ImmutableArray<ProjectileEvent>.Builder projectileEvents = ImmutableArray.CreateBuilder<ProjectileEvent>();

        foreach (ProjectileComponent projectile in zone.Projectiles.OrderBy(p => p.ProjectileId))
        {
            SkillDefinition? skill = FindSkill(config, projectile.SkillId);
            if (skill is null)
            {
                projectileEvents.Add(new ProjectileEvent(tick, projectile.ProjectileId, ProjectileEventKind.Despawn, projectile.OwnerId, projectile.TargetId, projectile.PosX, projectile.PosY));
                continue;
            }

            if (tick - projectile.SpawnTick >= projectile.MaxLifetimeTicks)
            {
                projectileEvents.Add(new ProjectileEvent(tick, projectile.ProjectileId, ProjectileEventKind.Despawn, projectile.OwnerId, projectile.TargetId, projectile.PosX, projectile.PosY));
                continue;
            }

            Fix32 step = new Fix32(skill.Value.ProjectileSpeedRaw);
            Fix32 dx = projectile.TargetX - projectile.PosX;
            Fix32 dy = projectile.TargetY - projectile.PosY;
            bool reaches = Fix32.Abs(dx) <= step && Fix32.Abs(dy) <= step;

            Fix32 nextX = reaches ? projectile.TargetX : projectile.PosX + Fix32.Clamp(dx, -step, step);
            Fix32 nextY = reaches ? projectile.TargetY : projectile.PosY + Fix32.Clamp(dy, -step, step);

            if (projectile.CollidesWithWorld)
            {
                int tileX = Fix32.FloorToInt(nextX);
                int tileY = Fix32.FloorToInt(nextY);
                if (zone.Map.Get(tileX, tileY) == TileKind.Solid)
                {
                    projectileEvents.Add(new ProjectileEvent(tick, projectile.ProjectileId, ProjectileEventKind.Despawn, projectile.OwnerId, projectile.TargetId, nextX, nextY));
                    continue;
                }
            }

            if (!reaches)
            {
                kept.Add(projectile with { PosX = nextX, PosY = nextY });
                continue;
            }

            if (projectile.TargetId.Value != 0)
            {
                int targetIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, projectile.TargetId);
                if (targetIndex >= 0)
                {
                    EntityState target = (updates.TryGetValue(targetIndex, out EntityState? updated) && updated is not null)
                        ? updated
                        : entities[targetIndex];
                    if (target.IsAlive)
                    {
                        int rawAmount = Math.Max(0, skill.Value.BaseDamage);
                        int defense = skill.Value.DamageType == DamageType.Magical
                            ? Math.Max(0, target.DefenseStats.MagicResist)
                            : Math.Max(0, target.DefenseStats.Armor);
                        int finalAmount = Math.Max(0, rawAmount - defense);
                        int nextHp = Math.Max(0, target.Hp - finalAmount);
                        int appliedDamage = target.Hp - nextHp;
                        EntityState updatedTarget = target with { Hp = nextHp, IsAlive = nextHp > 0 };
                        updates[targetIndex] = updatedTarget;

                        combatEvents.Add(new CombatEvent(tick, projectile.OwnerId, projectile.TargetId, projectile.SkillId, CombatEventType.Damage, appliedDamage));
                        combatLogEvents.Add(new CombatLogEvent(tick, projectile.OwnerId, projectile.TargetId, projectile.SkillId, rawAmount, appliedDamage, CombatLogKind.Damage));
                        if (nextHp == 0)
                        {
                            combatLogEvents.Add(new CombatLogEvent(tick, projectile.OwnerId, projectile.TargetId, projectile.SkillId, rawAmount, appliedDamage, CombatLogKind.Kill));
                        }
                        projectileEvents.Add(new ProjectileEvent(tick, projectile.ProjectileId, ProjectileEventKind.Hit, projectile.OwnerId, projectile.TargetId, nextX, nextY));
                        continue;
                    }
                }
            }

            projectileEvents.Add(new ProjectileEvent(tick, projectile.ProjectileId, ProjectileEventKind.Despawn, projectile.OwnerId, projectile.TargetId, nextX, nextY));
        }

        ImmutableArray<EntityState>.Builder rebuiltEntities = ImmutableArray.CreateBuilder<EntityState>(entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            rebuiltEntities.Add((updates.TryGetValue(i, out EntityState? next) && next is not null) ? next : entities[i]);
        }

        ZoneState updatedZone = zone
            .WithEntities(rebuiltEntities.ToImmutable().OrderBy(e => e.Id.Value).ToImmutableArray())
            .WithProjectiles(kept.ToImmutable().OrderBy(p => p.ProjectileId).ToImmutableArray());

        return (updatedZone, combatEvents.ToImmutable(), combatLogEvents.ToImmutable(), projectileEvents.ToImmutable());
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
