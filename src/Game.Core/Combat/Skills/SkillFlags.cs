namespace Game.Core;

[Flags]
public enum SkillFlags : uint
{
    None = 0,
    Melee = 1u << 0,
    Ranged = 1u << 1,
    AoE = 1u << 2,
    RequiresLineOfSight = 1u << 3
}
