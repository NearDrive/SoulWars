using Game.Protocol;

namespace Game.Client.Headless.Runtime;

public sealed record ClientRunResult(
    IReadOnlyList<string> Logs,
    IReadOnlyList<InputCommand> SentInputs,
    IReadOnlyList<CastSkillCommand> SentCasts,
    IReadOnlyList<HitEventV1> ObservedHits,
    bool HandshakeAccepted,
    int TotalTicks,
    int TotalHitEvents,
    string TraceHash,
    string CanonicalTrace)
{
    public bool HitObserved => ObservedHits.Count > 0;

    public bool HandshakeOk => HandshakeAccepted;

    public int TicksProcessed => TotalTicks;

    public int HitEventsSeen => TotalHitEvents;
}
