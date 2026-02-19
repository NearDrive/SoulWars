namespace Game.Core;

public enum CombatLogKind : byte
{
    Damage = 1,
    Kill = 2
}

public readonly record struct CombatLogEvent(
    int Tick,
    EntityId SourceId,
    EntityId TargetId,
    SkillId SkillId,
    int RawAmount,
    int FinalAmount,
    CombatLogKind Kind);
