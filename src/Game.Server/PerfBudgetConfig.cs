namespace Game.Server;

public readonly record struct PerfBudgetConfig(
    int MaxAoiChecksPerTick,
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
        MaxCollisionChecksPerTick: 2_000,
        MaxSnapshotsEncodedEntitiesPerTick: 6_000,
        MaxOutboundBytesPerTick: 1_200_000,
        MaxInboundBytesPerTick: 100_000,
        MaxCommandsProcessedPerTick: 600,
        MaxOutboundMessagesPerTick: 600,
        MaxInboundMessagesPerTick: 600,
        WindowTicks: 1_000);
}
