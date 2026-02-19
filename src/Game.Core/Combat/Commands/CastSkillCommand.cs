namespace Game.Core;

public readonly record struct CastSkillCommand(
    EntityId CasterId,
    SkillId SkillId,
    SkillTargetType TargetType,
    EntityId TargetEntityId,
    Fix32 TargetX,
    Fix32 TargetY);
