using System.Linq;
using System.Collections.Immutable;

namespace Game.Core;

public static class EncounterSystem
{
    public static (WorldState State, ImmutableArray<WorldCommand> GeneratedCommands) Step(SimulationConfig config, WorldState state)
    {
        EncounterRegistry registry = state.EncounterRegistryOrEmpty.Canonicalize();
        if (registry.Definitions.IsDefaultOrEmpty)
        {
            return (state, ImmutableArray<WorldCommand>.Empty);
        }

        WorldState updated = state;
        List<WorldCommand> commands = new();
        ImmutableArray<EncounterRuntimeState>.Builder runtimes = registry.RuntimeStates.ToBuilder();

        foreach (EncounterDefinition definition in registry.Definitions)
        {
            int runtimeIndex = FindRuntimeIndex(runtimes, definition.Id);
            EncounterRuntimeState runtime = runtimeIndex >= 0
                ? runtimes[runtimeIndex]
                : EncounterRuntimeState.Create(definition, state.Tick, FindDefaultBossEntityId(definition.ZoneId, state), instanceId: default);

            if (definition.PhasesOrEmpty.IsDefaultOrEmpty || runtime.CurrentPhase < 0 || runtime.CurrentPhase >= definition.PhasesOrEmpty.Length)
            {
                UpsertRuntime(runtimes, runtime);
                continue;
            }

            EncounterPhaseDefinition phase = definition.PhasesOrEmpty[runtime.CurrentPhase];
            for (int triggerIndex = 0; triggerIndex < phase.TriggersOrEmpty.Length; triggerIndex++)
            {
                if (runtime.HasFired(triggerIndex))
                {
                    continue;
                }

                EncounterTriggerDefinition trigger = phase.TriggersOrEmpty[triggerIndex];
                if (!ShouldFire(trigger, definition.ZoneId, updated, runtime))
                {
                    continue;
                }

                EncounterRuntimeState beforeActions = runtime;
                runtime = runtime.MarkFired(triggerIndex);

                for (int actionIndex = 0; actionIndex < trigger.ActionsOrEmpty.Length; actionIndex++)
                {
                    EncounterActionDefinition action = trigger.ActionsOrEmpty[actionIndex];
                    (updated, runtime, WorldCommand? generated) = ExecuteAction(config, updated, definition, runtime, triggerIndex, actionIndex, action);
                    if (generated is not null)
                    {
                        commands.Add(generated);
                    }
                }

                if (runtime.CurrentPhase != beforeActions.CurrentPhase)
                {
                    break;
                }
            }

            UpsertRuntime(runtimes, runtime);
        }

        updated = updated.WithEncounterRegistry(new EncounterRegistry(registry.Definitions, runtimes.ToImmutable()));
        return (updated, commands.ToImmutableArray());
    }

    private static (WorldState State, EncounterRuntimeState Runtime, WorldCommand? GeneratedCommand) ExecuteAction(
        SimulationConfig config,
        WorldState state,
        EncounterDefinition definition,
        EncounterRuntimeState runtime,
        int triggerIndex,
        int actionIndex,
        EncounterActionDefinition action)
    {
        switch (action.Kind)
        {
            case EncounterActionKind.SpawnNpc:
                return (SpawnNpc(config, state, definition, runtime, triggerIndex, actionIndex, action), runtime, null);
            case EncounterActionKind.CastSkill:
            {
                if (!TryResolveEntity(action.Caster, definition.ZoneId, state, runtime, out EntityState caster))
                {
                    return (state, runtime, null);
                }

                WorldCommand cmd = new(
                    Kind: WorldCommandKind.CastSkill,
                    EntityId: caster.Id,
                    ZoneId: definition.ZoneId,
                    SkillId: action.SkillId,
                    TargetKind: action.Target.Kind,
                    TargetEntityId: action.Target.Kind == CastTargetKind.Entity && TryResolveEntity(action.Target.EntityRef, definition.ZoneId, state, runtime, out EntityState target)
                        ? target.Id
                        : null,
                    TargetPosXRaw: action.Target.Kind == CastTargetKind.Point ? action.Target.X.Raw : 0,
                    TargetPosYRaw: action.Target.Kind == CastTargetKind.Point ? action.Target.Y.Raw : 0);
                return (state, runtime, cmd);
            }
            case EncounterActionKind.ApplyStatus:
                return (ApplyStatus(state, definition.ZoneId, runtime, action), runtime, null);
            case EncounterActionKind.SetPhase:
                return (state, runtime.ChangePhase(definition, action.PhaseIndex), null);
            default:
                return (state, runtime, null);
        }
    }

    private static WorldState SpawnNpc(
        SimulationConfig config,
        WorldState state,
        EncounterDefinition definition,
        EncounterRuntimeState runtime,
        int triggerIndex,
        int actionIndex,
        EncounterActionDefinition action)
    {
        if (action.Count <= 0 || !state.TryGetZone(definition.ZoneId, out ZoneState zone))
        {
            return state;
        }

        ImmutableArray<EntityState> entities = zone.Entities;
        ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(entities.Length + action.Count);
        builder.AddRange(entities);

        HashSet<int> usedIds = entities.Select(e => e.Id.Value).ToHashSet();
        for (int i = 0; i < action.Count; i++)
        {
            EntityId entityId = DeriveSpawnEntityId(definition.Id, triggerIndex, actionIndex, i);
            while (usedIds.Contains(entityId.Value))
            {
                entityId = new EntityId(entityId.Value == int.MaxValue ? 1 : entityId.Value + 1);
            }

            usedIds.Add(entityId.Value);
            builder.Add(new EntityState(
                Id: entityId,
                Pos: new Vec2Fix(action.X, action.Y),
                Vel: Vec2Fix.Zero,
                MaxHp: 100,
                Hp: 100,
                IsAlive: true,
                AttackRange: Fix32.FromInt(1),
                AttackDamage: 10,
                AttackCooldownTicks: 10,
                LastAttackTick: -10,
                DefenseStats: new DefenseStatsComponent(0, 0),
                Kind: EntityKind.Npc,
                NextWanderChangeTick: state.Tick + config.NpcWanderPeriodTicks,
                WanderX: 0,
                WanderY: 0));
        }

        ZoneState updatedZone = zone.WithEntities(builder.ToImmutable());
        return state.WithZoneUpdated(updatedZone);
    }

    private static WorldState ApplyStatus(WorldState state, ZoneId zoneId, EncounterRuntimeState runtime, EncounterActionDefinition action)
    {
        if (!state.TryGetZone(zoneId, out ZoneState zone))
        {
            return state;
        }

        if (!TryResolveEntity(action.StatusSource, zoneId, state, runtime, out EntityState source) ||
            !TryResolveEntity(action.StatusTarget, zoneId, state, runtime, out EntityState target))
        {
            return state;
        }

        int targetIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, target.Id);
        if (targetIndex < 0)
        {
            return state;
        }

        ImmutableArray<StatusEvent>.Builder statusEvents = ImmutableArray.CreateBuilder<StatusEvent>();
        StatusEffectInstance effect = new(
            action.StatusType,
            source.Id,
            state.Tick + Math.Max(1, action.StatusDurationTicks),
            action.StatusMagnitudeRaw);

        StatusEffectsComponent updatedStatus = target.StatusEffects.ApplyOrRefresh(effect, state.Tick, target.Id, statusEvents);

        ImmutableArray<EntityState> entities = zone.Entities;
        ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            builder.Add(i == targetIndex ? entities[i] with { StatusEffects = updatedStatus } : entities[i]);
        }

        return state.WithZoneUpdated(zone.WithEntities(builder.ToImmutable())).WithStatusEvents((state.StatusEvents.IsDefault ? ImmutableArray<StatusEvent>.Empty : state.StatusEvents).AddRange(statusEvents));
    }

    private static bool ShouldFire(EncounterTriggerDefinition trigger, ZoneId zoneId, WorldState state, EncounterRuntimeState runtime)
    {
        switch (trigger.Kind)
        {
            case EncounterTriggerKind.OnTick:
                return state.Tick - runtime.StartTick == trigger.AtTickOffset;
            case EncounterTriggerKind.OnHpBelowPct:
            {
                if (!TryResolveEntity(trigger.Target, zoneId, state, runtime, out EntityState target) || target.MaxHp <= 0)
                {
                    return false;
                }

                return (long)target.Hp * 100L < (long)target.MaxHp * trigger.Pct;
            }
            case EncounterTriggerKind.OnEntityDeath:
                return !TryResolveEntity(trigger.Target, zoneId, state, runtime, out _);
            default:
                return false;
        }
    }

    private static bool TryResolveEntity(EntityRef entityRef, ZoneId zoneId, WorldState state, EncounterRuntimeState runtime, out EntityState entity)
    {
        entity = null!;
        if (!state.TryGetZone(zoneId, out ZoneState zone))
        {
            return false;
        }

        EntityId id = entityRef.Kind == EntityRefKind.Boss ? runtime.BossEntityId : entityRef.EntityId;
        if (id.Value <= 0)
        {
            return false;
        }

        int index = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, id);
        if (index < 0)
        {
            return false;
        }

        entity = zone.Entities[index];
        return true;
    }

    private static EntityId DeriveSpawnEntityId(EncounterId encounterId, int triggerIndex, int actionIndex, int spawnOrdinal)
    {
        uint mixed = 2166136261u;
        mixed = (mixed ^ unchecked((uint)encounterId.Value)) * 16777619u;
        mixed = (mixed ^ unchecked((uint)(encounterId.Value >> 32))) * 16777619u;
        mixed = (mixed ^ unchecked((uint)triggerIndex)) * 16777619u;
        mixed = (mixed ^ unchecked((uint)actionIndex)) * 16777619u;
        mixed = (mixed ^ unchecked((uint)spawnOrdinal)) * 16777619u;
        int id = (int)(mixed & 0x7FFFFFFF);
        return new EntityId(id == 0 ? 1 : id);
    }



    private static EntityId FindDefaultBossEntityId(ZoneId zoneId, WorldState state)
    {
        if (!state.TryGetZone(zoneId, out ZoneState zone))
        {
            return default;
        }

        foreach (EntityState entity in zone.Entities.OrderBy(e => e.Id.Value))
        {
            if (entity.Kind == EntityKind.Npc)
            {
                return entity.Id;
            }
        }

        return default;
    }
    private static int FindRuntimeIndex(ImmutableArray<EncounterRuntimeState>.Builder runtimes, EncounterId id)
    {
        for (int i = 0; i < runtimes.Count; i++)
        {
            if (runtimes[i].EncounterId.Value == id.Value)
            {
                return i;
            }
        }

        return -1;
    }

    private static void UpsertRuntime(ImmutableArray<EncounterRuntimeState>.Builder runtimes, EncounterRuntimeState runtime)
    {
        int idx = FindRuntimeIndex(runtimes, runtime.EncounterId);
        if (idx >= 0)
        {
            runtimes[idx] = runtime;
            return;
        }

        runtimes.Add(runtime);
    }
}
