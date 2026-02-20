using System.Linq;
using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct EncounterId(ulong Value);

public enum EntityRefKind : byte
{
    Boss = 1,
    EntityId = 2
}

public readonly record struct EntityRef(EntityRefKind Kind, EntityId EntityId)
{
    public static EntityRef Boss => new(EntityRefKind.Boss, default);
    public static EntityRef FromEntityId(EntityId entityId) => new(EntityRefKind.EntityId, entityId);
}

public readonly record struct TargetSpec(CastTargetKind Kind, EntityRef EntityRef, Fix32 X, Fix32 Y)
{
    public static TargetSpec Self => new(CastTargetKind.Self, default, default, default);
    public static TargetSpec Entity(EntityRef entityRef) => new(CastTargetKind.Entity, entityRef, default, default);
    public static TargetSpec Point(Fix32 x, Fix32 y) => new(CastTargetKind.Point, default, x, y);
}

public enum EncounterTriggerKind : byte
{
    OnTick = 1,
    OnHpBelowPct = 2,
    OnEntityDeath = 3
}

public enum EncounterActionKind : byte
{
    SpawnNpc = 1,
    CastSkill = 2,
    ApplyStatus = 3,
    SetPhase = 4
}

public sealed record EncounterActionDefinition(
    EncounterActionKind Kind,
    string NpcArchetypeId = "",
    Fix32 X = default,
    Fix32 Y = default,
    int Count = 0,
    EntityRef Caster = default,
    SkillId SkillId = default,
    TargetSpec Target = default,
    EntityRef StatusSource = default,
    EntityRef StatusTarget = default,
    StatusEffectType StatusType = StatusEffectType.Slow,
    int StatusDurationTicks = 0,
    int StatusMagnitudeRaw = 0,
    int PhaseIndex = 0);

public sealed record EncounterTriggerDefinition(
    EncounterTriggerKind Kind,
    int AtTickOffset = 0,
    EntityRef Target = default,
    int Pct = 0,
    ImmutableArray<EncounterActionDefinition> Actions = default)
{
    public ImmutableArray<EncounterActionDefinition> ActionsOrEmpty => Actions.IsDefault ? ImmutableArray<EncounterActionDefinition>.Empty : Actions;
}

public sealed record EncounterPhaseDefinition(ImmutableArray<EncounterTriggerDefinition> Triggers)
{
    public ImmutableArray<EncounterTriggerDefinition> TriggersOrEmpty => Triggers.IsDefault ? ImmutableArray<EncounterTriggerDefinition>.Empty : Triggers;
}

public sealed record EncounterDefinition(
    EncounterId Id,
    string Key,
    int Version,
    ZoneId ZoneId,
    ImmutableArray<EncounterPhaseDefinition> Phases)
{
    public ImmutableArray<EncounterPhaseDefinition> PhasesOrEmpty => Phases.IsDefault ? ImmutableArray<EncounterPhaseDefinition>.Empty : Phases;
}

public sealed record EncounterRuntimeState(
    EncounterId EncounterId,
    int CurrentPhase,
    int StartTick,
    ImmutableArray<bool> FiredTriggers,
    EntityId BossEntityId,
    ZoneInstanceId InstanceId)
{
    public static EncounterRuntimeState Create(EncounterDefinition definition, int startTick, EntityId bossEntityId, ZoneInstanceId instanceId)
    {
        int triggerCount = definition.PhasesOrEmpty.IsDefaultOrEmpty || definition.PhasesOrEmpty[0].TriggersOrEmpty.IsDefault
            ? 0
            : definition.PhasesOrEmpty[0].TriggersOrEmpty.Length;
        return new EncounterRuntimeState(definition.Id, 0, startTick, ImmutableArray.CreateRange(Enumerable.Repeat(false, triggerCount)), bossEntityId, instanceId);
    }

    public bool HasFired(int triggerIndex) => triggerIndex >= 0 && triggerIndex < FiredTriggers.Length && FiredTriggers[triggerIndex];

    public EncounterRuntimeState MarkFired(int triggerIndex)
    {
        if (triggerIndex < 0 || triggerIndex >= FiredTriggers.Length)
        {
            return this;
        }

        ImmutableArray<bool>.Builder flags = FiredTriggers.ToBuilder();
        flags[triggerIndex] = true;
        return this with { FiredTriggers = flags.MoveToImmutable() };
    }

    public EncounterRuntimeState ChangePhase(EncounterDefinition definition, int phaseIndex)
    {
        if (phaseIndex < 0 || phaseIndex >= definition.PhasesOrEmpty.Length)
        {
            return this;
        }

        int triggerCount = definition.PhasesOrEmpty[phaseIndex].TriggersOrEmpty.Length;
        return this with
        {
            CurrentPhase = phaseIndex,
            FiredTriggers = ImmutableArray.CreateRange(Enumerable.Repeat(false, triggerCount))
        };
    }
}

public sealed record EncounterRegistry(
    ImmutableArray<EncounterDefinition> Definitions,
    ImmutableArray<EncounterRuntimeState> RuntimeStates)
{
    public static EncounterRegistry Empty => new(ImmutableArray<EncounterDefinition>.Empty, ImmutableArray<EncounterRuntimeState>.Empty);

    public EncounterRegistry Canonicalize()
    {
        return this with
        {
            Definitions = (Definitions.IsDefault ? ImmutableArray<EncounterDefinition>.Empty : Definitions)
                .OrderBy(e => e.Id.Value)
                .ToImmutableArray(),
            RuntimeStates = (RuntimeStates.IsDefault ? ImmutableArray<EncounterRuntimeState>.Empty : RuntimeStates)
                .OrderBy(e => e.EncounterId.Value)
                .ToImmutableArray()
        };
    }
}
