namespace Game.Core;

public static class SkillCastSystem
{
    public static CastSkillCommand FromWorldCommand(WorldCommand command)
    {
        return new CastSkillCommand(
            CasterId: command.EntityId,
            SkillId: command.SkillId ?? default,
            TargetType: (SkillTargetType)command.TargetKind,
            TargetEntityId: command.TargetEntityId ?? default,
            TargetX: new Fix32(command.TargetPosXRaw),
            TargetY: new Fix32(command.TargetPosYRaw));
    }

    public static bool HasCoherentTarget(CastSkillCommand command)
    {
        switch ((CastTargetKind)command.TargetType)
        {
            case CastTargetKind.Self:
                return command.TargetEntityId.Value == 0 && command.TargetX.Raw == 0 && command.TargetY.Raw == 0;
            case CastTargetKind.Entity:
                return command.TargetEntityId.Value != 0 && command.TargetX.Raw == 0 && command.TargetY.Raw == 0;
            case CastTargetKind.Point:
                return command.TargetEntityId.Value == 0;
            default:
                return false;
        }
    }

    public static SkillCastIntent ToIntent(int tick, CastSkillCommand command)
    {
        return new SkillCastIntent(
            Tick: tick,
            CasterId: command.CasterId,
            SkillId: command.SkillId,
            TargetType: command.TargetType,
            TargetEntityId: command.TargetEntityId,
            TargetX: command.TargetX,
            TargetY: command.TargetY);
    }
}
