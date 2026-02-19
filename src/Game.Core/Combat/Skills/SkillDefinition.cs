namespace Game.Core;

public readonly record struct SkillDefinition(
    SkillId Id,
    int RangeRaw,
    int HitRadiusRaw,
    int MaxTargets,
    int CooldownTicks,
    int CastTimeTicks,
    int GlobalCooldownTicks,
    int ResourceCost,
    SkillTargetType TargetType,
    SkillFlags Flags = SkillFlags.None,
    SkillEffectKind EffectKind = SkillEffectKind.Damage,
    int BaseAmount = 0,
    int CoefRaw = 0,
    OptionalStatusEffect? StatusEffect = null,
    int BaseDamage = 0,
    DamageType DamageType = DamageType.Physical)
{
    public int RangeQRaw => RangeRaw;

    public CastTargetKind TargetKind => (CastTargetKind)TargetType;


    public SkillDefinition(
        SkillId Id,
        int RangeQRaw,
        int HitRadiusRaw,
        int MaxTargets,
        int CooldownTicks,
        int CastTimeTicks,
        int GlobalCooldownTicks,
        int ResourceCost,
        CastTargetKind TargetKind,
        SkillEffectKind EffectKind = SkillEffectKind.Damage,
        int BaseAmount = 0,
        int CoefRaw = 0,
        OptionalStatusEffect? StatusEffect = null,
        int BaseDamage = 0,
        DamageType DamageType = DamageType.Physical)
        : this(Id, RangeQRaw, HitRadiusRaw, MaxTargets, CooldownTicks, CastTimeTicks, GlobalCooldownTicks, ResourceCost, (SkillTargetType)TargetKind, SkillFlags.None, EffectKind, BaseAmount, CoefRaw, StatusEffect, BaseDamage, DamageType)
    {
    }

    public SkillDefinition(
        SkillId Id,
        int RangeQRaw,
        int HitRadiusRaw,
        int CooldownTicks,
        int CastTimeTicks,
        int GlobalCooldownTicks,
        int ResourceCost,
        CastTargetKind TargetKind,
        SkillEffectKind EffectKind = SkillEffectKind.Damage,
        int BaseAmount = 0,
        int CoefRaw = 0,
        OptionalStatusEffect? StatusEffect = null,
        int BaseDamage = 0,
        DamageType DamageType = DamageType.Physical)
        : this(Id, RangeQRaw, HitRadiusRaw, MaxTargets: 8, CooldownTicks, CastTimeTicks, GlobalCooldownTicks, ResourceCost, (SkillTargetType)TargetKind, SkillFlags.None, EffectKind, BaseAmount, CoefRaw, StatusEffect, BaseDamage, DamageType)
    {
    }

    public SkillDefinition(
        SkillId Id,
        int RangeQRaw,
        int HitRadiusRaw,
        int CooldownTicks,
        int ResourceCost,
        CastTargetKind TargetKind,
        SkillEffectKind EffectKind = SkillEffectKind.Damage,
        int BaseAmount = 0,
        int CoefRaw = 0,
        OptionalStatusEffect? StatusEffect = null,
        int BaseDamage = 0,
        DamageType DamageType = DamageType.Physical)
        : this(Id, RangeQRaw, HitRadiusRaw, CooldownTicks, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost, TargetKind, EffectKind, BaseAmount, CoefRaw, StatusEffect, BaseDamage, DamageType)
    {
    }
}
