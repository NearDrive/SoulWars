using System.Text.Json;

namespace Game.Server;

public static class PerfReportWriter
{
    public static string ToJson(PerfSnapshot snapshot, PerfBudgetConfig budget, BudgetResult result)
    {
        object payload = new
        {
            tickCount = snapshot.TickCount,
            totals = new
            {
                entitiesVisited = snapshot.TotalEntitiesVisited,
                aoiDistanceChecks = snapshot.TotalAoiDistanceChecks,
                collisionChecks = snapshot.TotalCollisionChecks,
                commandsProcessed = snapshot.TotalCommandsProcessed,
                snapshotsEncodedEntities = snapshot.TotalSnapshotsEncodedEntities,
                outboundBytes = snapshot.TotalOutboundBytes,
                inboundBytes = snapshot.TotalInboundBytes,
                outboundMessages = snapshot.TotalOutboundMessages,
                inboundMessages = snapshot.TotalInboundMessages
            },
            maxPerTick = new
            {
                entitiesVisited = snapshot.MaxEntitiesVisitedPerTick,
                aoiDistanceChecks = snapshot.MaxAoiDistanceChecksPerTick,
                collisionChecks = snapshot.MaxCollisionChecksPerTick,
                commandsProcessed = snapshot.MaxCommandsProcessedPerTick,
                snapshotsEncodedEntities = snapshot.MaxSnapshotsEncodedEntitiesPerTick,
                outboundBytes = snapshot.MaxOutboundBytesPerTick,
                inboundBytes = snapshot.MaxInboundBytesPerTick,
                outboundMessages = snapshot.MaxOutboundMessagesPerTick,
                inboundMessages = snapshot.MaxInboundMessagesPerTick
            },
            avgPerTick = new
            {
                entitiesVisited = snapshot.AvgEntitiesVisitedPerTick,
                aoiDistanceChecks = snapshot.AvgAoiDistanceChecksPerTick,
                collisionChecks = snapshot.AvgCollisionChecksPerTick,
                commandsProcessed = snapshot.AvgCommandsProcessedPerTick,
                snapshotsEncodedEntities = snapshot.AvgSnapshotsEncodedEntitiesPerTick,
                outboundBytes = snapshot.AvgOutboundBytesPerTick,
                inboundBytes = snapshot.AvgInboundBytesPerTick,
                outboundMessages = snapshot.AvgOutboundMessagesPerTick,
                inboundMessages = snapshot.AvgInboundMessagesPerTick
            },
            budgets = budget,
            ok = result.Ok,
            violations = result.Violations
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
