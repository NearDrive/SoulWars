using System.Collections.Immutable;

namespace Game.Core;

public static class CombatEventBudgets
{
    public const int DefaultMaxCombatEventsPerTickPerZone = 64;
    public const int DefaultMaxCombatEventsRetainedPerZone = 512;
    public const int DefaultMaxCombatEventsPerSnapshot = 32;
    public const int DefaultMaxCombatLogEventsRetained = 512;

    public static ImmutableArray<CombatEvent> OrderCanonically(ImmutableArray<CombatEvent> events)
    {
        return (events.IsDefault ? ImmutableArray<CombatEvent>.Empty : events)
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.SourceId.Value)
            .ThenBy(e => e.TargetId.Value)
            .ThenBy(e => e.SkillId.Value)
            .ThenBy(e => (int)e.Type)
            .ThenBy(e => e.Amount)
            .ToImmutableArray();
    }


    public static ImmutableArray<CombatEvent> TakeSnapshotEvents(ImmutableArray<CombatEvent> events, int maxCombatEventsPerSnapshot)
    {
        ImmutableArray<CombatEvent> ordered = OrderCanonically(events);
        if (maxCombatEventsPerSnapshot <= 0 || ordered.Length <= maxCombatEventsPerSnapshot)
        {
            return ordered;
        }

        return ordered.Take(maxCombatEventsPerSnapshot).ToImmutableArray();
    }

    public static bool IsCanonicalOrder(ImmutableArray<CombatEvent> events)
    {
        ImmutableArray<CombatEvent> source = events.IsDefault ? ImmutableArray<CombatEvent>.Empty : events;
        for (int i = 1; i < source.Length; i++)
        {
            if (CompareCanonical(source[i - 1], source[i]) > 0)
            {
                return false;
            }
        }

        return true;
    }

    public static int CompareCanonical(CombatEvent left, CombatEvent right)
    {
        int cmp = left.Tick.CompareTo(right.Tick);
        if (cmp != 0) return cmp;
        cmp = left.SourceId.Value.CompareTo(right.SourceId.Value);
        if (cmp != 0) return cmp;
        cmp = left.TargetId.Value.CompareTo(right.TargetId.Value);
        if (cmp != 0) return cmp;
        cmp = left.SkillId.Value.CompareTo(right.SkillId.Value);
        if (cmp != 0) return cmp;
        cmp = ((int)left.Type).CompareTo((int)right.Type);
        if (cmp != 0) return cmp;
        return left.Amount.CompareTo(right.Amount);
    }
}
