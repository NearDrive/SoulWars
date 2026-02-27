using Game.Server;

namespace Game.Server.Tests;

internal static class Pr85PerfBudgets
{
    // These limits are derived from deterministic fixture bounds:
    // fixed map size, fixed entity count, fixed vision radius and fixed tick count.
    public const int TickCount = 30;

    public const int MaxVisibilityCellsVisitedPerTick = 2_000;
    public const int MaxVisibilityRaysCastPerTick = 2_000;
    public const int MaxEntitiesConsideredPerTick = 64;
    public const long MaxEntitiesConsideredPerSession = 2_000;
    public const int MaxEntitiesEmittedPerTick = 128;
    public const long MaxEntitiesEmittedPerSession = 4_000;
    public const int MaxTransitionSpawnsPerTick = 32;
    public const int MaxTransitionDespawnsPerTick = 32;

    public static PerfBudgetConfig BuildBudget() => new(
        MaxAoiChecksPerTick: MaxEntitiesConsideredPerTick,
        MaxVisibilityCellsVisitedPerTick: MaxVisibilityCellsVisitedPerTick,
        MaxVisibilityRaysCastPerTick: MaxVisibilityRaysCastPerTick,
        MaxEntitiesConsideredPerTick: MaxEntitiesConsideredPerTick,
        MaxEntitiesEmittedPerTick: MaxEntitiesEmittedPerTick,
        MaxTransitionSpawnsPerTick: MaxTransitionSpawnsPerTick,
        MaxTransitionDespawnsPerTick: MaxTransitionDespawnsPerTick,
        MaxEntitiesConsideredPerSession: MaxEntitiesConsideredPerSession,
        MaxEntitiesEmittedPerSession: MaxEntitiesEmittedPerSession,
        MaxCollisionChecksPerTick: 20_000,
        MaxSnapshotsEncodedEntitiesPerTick: 10_000,
        MaxOutboundBytesPerTick: 2_000_000,
        MaxInboundBytesPerTick: 500_000,
        MaxCommandsProcessedPerTick: 1_000,
        MaxOutboundMessagesPerTick: 1_000,
        MaxInboundMessagesPerTick: 1_000,
        WindowTicks: TickCount);
}
