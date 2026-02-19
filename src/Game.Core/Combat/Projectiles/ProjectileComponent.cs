namespace Game.Core;

public readonly record struct ProjectileComponent(
    int ProjectileId,
    EntityId OwnerId,
    EntityId TargetId,
    Fix32 TargetX,
    Fix32 TargetY,
    Fix32 PosX,
    Fix32 PosY,
    SkillId SkillId,
    int SpawnTick,
    int MaxLifetimeTicks,
    bool CollidesWithWorld,
    bool RequiresLoSOnSpawn);
