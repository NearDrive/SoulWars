using System.Collections.Immutable;

namespace Game.Core;

public static class SkillEffectSystem
{
    public const int MaxCombatLogEventsPerTick = 256;

    public static (ZoneState Zone, ImmutableArray<CombatLogEvent> Events) ApplyPendingIntents(
        SimulationConfig config,
        int tick,
        ZoneState zone,
        ImmutableArray<SkillCastIntent> intents)
    {
        if (intents.IsDefaultOrEmpty)
        {
            return (zone, ImmutableArray<CombatLogEvent>.Empty);
        }

        ZoneState current = zone;
        ImmutableArray<CombatLogEvent>.Builder events = ImmutableArray.CreateBuilder<CombatLogEvent>();
        ImmutableArray<SkillCastIntent> ordered = intents
            .OrderBy(i => i.Tick)
            .ThenBy(i => i.CasterId.Value)
            .ThenBy(i => i.SkillId.Value)
            .ThenBy(i => (int)i.TargetType)
            .ThenBy(i => i.TargetEntityId.Value)
            .ThenBy(i => i.TargetX.Raw)
            .ThenBy(i => i.TargetY.Raw)
            .ToImmutableArray();

        foreach (SkillCastIntent intent in ordered)
        {
            if ((CastTargetKind)intent.TargetType != CastTargetKind.Entity)
            {
                continue;
            }

            SkillDefinition? skill = FindSkill(config, intent.SkillId);
            if (skill is null)
            {
                continue;
            }

            int sourceIndex = ZoneEntities.FindIndex(current.EntitiesData.AliveIds, intent.CasterId);
            int targetIndex = ZoneEntities.FindIndex(current.EntitiesData.AliveIds, intent.TargetEntityId);
            if (sourceIndex < 0 || targetIndex < 0)
            {
                continue;
            }

            ImmutableArray<EntityState> entities = current.Entities;
            EntityState target = entities[targetIndex];
            if (!target.IsAlive)
            {
                continue;
            }

            int damage = Math.Max(0, skill.Value.BaseDamage);
            int nextHp = Math.Max(0, target.Hp - damage);
            bool killed = target.IsAlive && nextHp == 0;
            EntityState updatedTarget = target with { Hp = nextHp, IsAlive = nextHp > 0 };

            ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                builder.Add(i == targetIndex ? updatedTarget : entities[i]);
            }

            current = current.WithEntities(builder.ToImmutable().OrderBy(e => e.Id.Value).ToImmutableArray());
            events.Add(new CombatLogEvent(tick, intent.CasterId, intent.TargetEntityId, intent.SkillId, damage, CombatLogKind.Damage));
            if (killed)
            {
                events.Add(new CombatLogEvent(tick, intent.CasterId, intent.TargetEntityId, intent.SkillId, damage, CombatLogKind.Kill));
            }

            if (events.Count >= MaxCombatLogEventsPerTick)
            {
                break;
            }
        }

        return (current, events.ToImmutable());
    }

    private static SkillDefinition? FindSkill(SimulationConfig config, SkillId skillId)
    {
        ImmutableArray<SkillDefinition> skills = config.SkillDefinitions.IsDefault
            ? ImmutableArray<SkillDefinition>.Empty
            : config.SkillDefinitions;

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
