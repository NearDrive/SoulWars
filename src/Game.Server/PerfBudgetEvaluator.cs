namespace Game.Server;

public static class PerfBudgetEvaluator
{
    public static BudgetResult Evaluate(PerfSnapshot snap, PerfBudgetConfig budget)
    {
        List<string> violations = new();

        if (snap.MaxAoiDistanceChecksPerTick > budget.MaxAoiChecksPerTick)
        {
            violations.Add($"AoiDistanceChecksPerTick exceeded: max={snap.MaxAoiDistanceChecksPerTick} budget={budget.MaxAoiChecksPerTick}");
        }

        if (snap.MaxCollisionChecksPerTick > budget.MaxCollisionChecksPerTick)
        {
            violations.Add($"CollisionChecksPerTick exceeded: max={snap.MaxCollisionChecksPerTick} budget={budget.MaxCollisionChecksPerTick}");
        }


        if (snap.MaxVisibilityCellsVisitedPerTick > budget.MaxVisibilityCellsVisitedPerTick)
        {
            violations.Add($"VisibilityCellsVisitedPerTick exceeded: max={snap.MaxVisibilityCellsVisitedPerTick} budget={budget.MaxVisibilityCellsVisitedPerTick}");
        }

        if (snap.MaxVisibilityRaysCastPerTick > budget.MaxVisibilityRaysCastPerTick)
        {
            violations.Add($"VisibilityRaysCastPerTick exceeded: max={snap.MaxVisibilityRaysCastPerTick} budget={budget.MaxVisibilityRaysCastPerTick}");
        }

        if (snap.MaxAoiEntitiesConsideredPerTick > budget.MaxEntitiesConsideredPerTick)
        {
            violations.Add($"AoiEntitiesConsideredPerTick exceeded: max={snap.MaxAoiEntitiesConsideredPerTick} budget={budget.MaxEntitiesConsideredPerTick}");
        }

        long maxEntitiesConsideredForWindow = ScaleSessionTotalBudget(budget.MaxEntitiesConsideredPerSession, snap.TickCount, budget.WindowTicks);
        if (snap.TotalAoiEntitiesConsidered > maxEntitiesConsideredForWindow)
        {
            violations.Add($"AoiEntitiesConsideredPerSession exceeded: total={snap.TotalAoiEntitiesConsidered} budget={maxEntitiesConsideredForWindow} baseWindowBudget={budget.MaxEntitiesConsideredPerSession} ticks={snap.TickCount} windowTicks={budget.WindowTicks}");
        }

        if (snap.MaxRedactionEntitiesEmittedPerTick > budget.MaxEntitiesEmittedPerTick)
        {
            violations.Add($"RedactionEntitiesEmittedPerTick exceeded: max={snap.MaxRedactionEntitiesEmittedPerTick} budget={budget.MaxEntitiesEmittedPerTick}");
        }

        long maxEntitiesEmittedForWindow = ScaleSessionTotalBudget(budget.MaxEntitiesEmittedPerSession, snap.TickCount, budget.WindowTicks);
        if (snap.TotalRedactionEntitiesEmitted > maxEntitiesEmittedForWindow)
        {
            violations.Add($"RedactionEntitiesEmittedPerSession exceeded: total={snap.TotalRedactionEntitiesEmitted} budget={maxEntitiesEmittedForWindow} baseWindowBudget={budget.MaxEntitiesEmittedPerSession} ticks={snap.TickCount} windowTicks={budget.WindowTicks}");
        }

        if (snap.MaxTransitionSpawnsPerTick > budget.MaxTransitionSpawnsPerTick)
        {
            violations.Add($"TransitionSpawnsPerTick exceeded: max={snap.MaxTransitionSpawnsPerTick} budget={budget.MaxTransitionSpawnsPerTick}");
        }

        if (snap.MaxTransitionDespawnsPerTick > budget.MaxTransitionDespawnsPerTick)
        {
            violations.Add($"TransitionDespawnsPerTick exceeded: max={snap.MaxTransitionDespawnsPerTick} budget={budget.MaxTransitionDespawnsPerTick}");
        }

        if (snap.MaxSnapshotsEncodedEntitiesPerTick > budget.MaxSnapshotsEncodedEntitiesPerTick)
        {
            violations.Add($"SnapshotsEncodedEntitiesPerTick exceeded: max={snap.MaxSnapshotsEncodedEntitiesPerTick} budget={budget.MaxSnapshotsEncodedEntitiesPerTick}");
        }

        if (snap.MaxOutboundBytesPerTick > budget.MaxOutboundBytesPerTick)
        {
            violations.Add($"OutboundBytesPerTick exceeded: max={snap.MaxOutboundBytesPerTick} budget={budget.MaxOutboundBytesPerTick}");
        }

        if (snap.MaxInboundBytesPerTick > budget.MaxInboundBytesPerTick)
        {
            violations.Add($"InboundBytesPerTick exceeded: max={snap.MaxInboundBytesPerTick} budget={budget.MaxInboundBytesPerTick}");
        }

        if (snap.MaxCommandsProcessedPerTick > budget.MaxCommandsProcessedPerTick)
        {
            violations.Add($"CommandsProcessedPerTick exceeded: max={snap.MaxCommandsProcessedPerTick} budget={budget.MaxCommandsProcessedPerTick}");
        }

        if (snap.MaxOutboundMessagesPerTick > budget.MaxOutboundMessagesPerTick)
        {
            violations.Add($"OutboundMessagesPerTick exceeded: max={snap.MaxOutboundMessagesPerTick} budget={budget.MaxOutboundMessagesPerTick}");
        }

        if (snap.MaxInboundMessagesPerTick > budget.MaxInboundMessagesPerTick)
        {
            violations.Add($"InboundMessagesPerTick exceeded: max={snap.MaxInboundMessagesPerTick} budget={budget.MaxInboundMessagesPerTick}");
        }

        if (snap.TickCount < budget.WindowTicks)
        {
            violations.Add($"Insufficient window ticks: observed={snap.TickCount} required={budget.WindowTicks}");
        }

        return new BudgetResult(violations.Count == 0, violations.ToArray());
    }

    private static long ScaleSessionTotalBudget(long budgetPerWindow, int observedTicks, int windowTicks)
    {
        if (budgetPerWindow <= 0)
        {
            return 0;
        }

        if (windowTicks <= 0 || observedTicks <= 0)
        {
            return budgetPerWindow;
        }

        long numerator = checked(budgetPerWindow * observedTicks);
        long denominator = windowTicks;
        return (numerator + denominator - 1) / denominator;
    }
}
