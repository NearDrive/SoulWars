namespace Game.Server;

public readonly record struct PerfSnapshot(
    int TickCount,
    long TotalEntitiesVisited,
    int MaxEntitiesVisitedPerTick,
    long TotalAoiDistanceChecks,
    int MaxAoiDistanceChecksPerTick,
    long TotalCollisionChecks,
    int MaxCollisionChecksPerTick,
    long TotalCommandsProcessed,
    int MaxCommandsProcessedPerTick,
    long TotalSnapshotsEncodedEntities,
    int MaxSnapshotsEncodedEntitiesPerTick,
    long TotalOutboundBytes,
    int MaxOutboundBytesPerTick,
    long TotalInboundBytes,
    int MaxInboundBytesPerTick,
    long TotalOutboundMessages,
    int MaxOutboundMessagesPerTick,
    long TotalInboundMessages,
    int MaxInboundMessagesPerTick)
{
    private static double Average(long total, int ticks) => ticks <= 0 ? 0d : total / (double)ticks;

    public double AvgEntitiesVisitedPerTick => Average(TotalEntitiesVisited, TickCount);

    public double AvgAoiDistanceChecksPerTick => Average(TotalAoiDistanceChecks, TickCount);

    public double AvgCollisionChecksPerTick => Average(TotalCollisionChecks, TickCount);

    public double AvgCommandsProcessedPerTick => Average(TotalCommandsProcessed, TickCount);

    public double AvgSnapshotsEncodedEntitiesPerTick => Average(TotalSnapshotsEncodedEntities, TickCount);

    public double AvgOutboundBytesPerTick => Average(TotalOutboundBytes, TickCount);

    public double AvgInboundBytesPerTick => Average(TotalInboundBytes, TickCount);

    public double AvgOutboundMessagesPerTick => Average(TotalOutboundMessages, TickCount);

    public double AvgInboundMessagesPerTick => Average(TotalInboundMessages, TickCount);
}
