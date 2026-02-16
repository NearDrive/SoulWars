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
}
