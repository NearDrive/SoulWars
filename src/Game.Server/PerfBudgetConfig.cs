namespace Game.Server;

public readonly record struct PerfBudgetConfig(
    int MaxAoiChecksPerTick,
    int MaxVisibilityCellsVisitedPerTick,
    int MaxVisibilityRaysCastPerTick,
    int MaxEntitiesConsideredPerTick,
    int MaxEntitiesEmittedPerTick,
    int MaxTransitionSpawnsPerTick,
    int MaxTransitionDespawnsPerTick,
    long MaxEntitiesConsideredPerSession,
    long MaxEntitiesEmittedPerSession,
    int MaxCollisionChecksPerTick,
    int MaxSnapshotsEncodedEntitiesPerTick,
    int MaxOutboundBytesPerTick,
    int MaxInboundBytesPerTick,
    int MaxCommandsProcessedPerTick,
    int MaxOutboundMessagesPerTick,
    int MaxInboundMessagesPerTick,
    int WindowTicks)
{
    public static PerfBudgetConfig Default => new(
        MaxAoiChecksPerTick: 3_500,
        MaxVisibilityCellsVisitedPerTick: 20_000,
        MaxVisibilityRaysCastPerTick: 20_000,
        MaxEntitiesConsideredPerTick: 3_500,
        MaxEntitiesEmittedPerTick: 6_000,
        MaxTransitionSpawnsPerTick: 64,
        MaxTransitionDespawnsPerTick: 64,
        MaxEntitiesConsideredPerSession: 3_500_000,
        MaxEntitiesEmittedPerSession: 6_000_000,
        MaxCollisionChecksPerTick: 2_000,
        MaxSnapshotsEncodedEntitiesPerTick: 6_000,
        MaxOutboundBytesPerTick: 1_200_000,
        MaxInboundBytesPerTick: 100_000,
        MaxCommandsProcessedPerTick: 600,
        MaxOutboundMessagesPerTick: 600,
        MaxInboundMessagesPerTick: 600,
        WindowTicks: 1_000);
}
