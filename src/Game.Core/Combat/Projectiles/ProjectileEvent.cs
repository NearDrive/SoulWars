namespace Game.Core;

public enum ProjectileEventKind : byte
{
    Spawn = 1,
    Hit = 2,
    Despawn = 3
}

public readonly record struct ProjectileEvent(
    int Tick,
    int ProjectileId,
    ProjectileEventKind Kind,
    EntityId OwnerId,
    EntityId TargetId,
    SkillId AbilityId,
    Fix32 PosX,
    Fix32 PosY);
