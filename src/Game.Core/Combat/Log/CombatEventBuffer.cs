using System.Collections.Immutable;

namespace Game.Core;

public static class CombatEventBuffer
{
    public static ImmutableArray<CombatEvent> AppendTickEvents(
        ImmutableArray<CombatEvent> retained,
        ImmutableArray<CombatEvent> tickEvents,
        int maxRetained)
    {
        ImmutableArray<CombatEvent> safeRetained = retained.IsDefault ? ImmutableArray<CombatEvent>.Empty : retained;
        ImmutableArray<CombatEvent> safeTick = tickEvents.IsDefault ? ImmutableArray<CombatEvent>.Empty : tickEvents;
        if (safeTick.IsDefaultOrEmpty)
        {
            return TrimRetained(safeRetained, maxRetained);
        }

        ImmutableArray<CombatEvent> combined = safeRetained.AddRange(safeTick);
        return TrimRetained(combined, maxRetained);
    }

    public static ImmutableArray<CombatEvent> TrimRetained(ImmutableArray<CombatEvent> events, int maxRetained)
    {
        ImmutableArray<CombatEvent> ordered = CombatEventBudgets.OrderCanonically(events);
        if (maxRetained <= 0 || ordered.Length <= maxRetained)
        {
            return ordered;
        }

        return ordered[^maxRetained..];
    }

    public static ImmutableArray<CombatLogEvent> TrimCombatLog(ImmutableArray<CombatLogEvent> events, int maxRetained)
    {
        ImmutableArray<CombatLogEvent> ordered = (events.IsDefault ? ImmutableArray<CombatLogEvent>.Empty : events)
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.SourceId.Value)
            .ThenBy(e => e.TargetId.Value)
            .ThenBy(e => e.SkillId.Value)
            .ThenBy(e => (int)e.Kind)
            .ThenBy(e => e.RawAmount)
            .ThenBy(e => e.FinalAmount)
            .ToImmutableArray();

        if (maxRetained <= 0 || ordered.Length <= maxRetained)
        {
            return ordered;
        }

        return ordered[^maxRetained..];
    }
}
