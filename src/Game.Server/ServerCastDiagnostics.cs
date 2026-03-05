namespace Game.Server;

public enum ServerCastDiagStage
{
    DecodeReject = 1,
    ValidateReject = 2,
    ApplyAccepted = 3,
    HitEmitted = 4,
    SelfAssigned = 5
}

public readonly record struct ServerCastDiagnosticsEvent(
    int Tick,
    int SessionId,
    ServerCastDiagStage Stage,
    int AbilityId,
    int ZoneId,
    int TargetPosXRaw,
    int TargetPosYRaw,
    int RawReasonCode,
    string RawReasonName,
    string Detail);
