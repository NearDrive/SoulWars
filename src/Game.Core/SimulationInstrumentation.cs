namespace Game.Core;

public sealed class SimulationInstrumentation
{
    public Action<int>? CountEntitiesVisited { get; init; }

    public Action<int>? CountCollisionChecks { get; init; }

    public Action<int>? CountVisibilityCellsVisited { get; init; }

    public Action<int>? CountVisibilityRaysCast { get; init; }

    public Action<ZoneTransferEvent>? OnZoneTransferQueued { get; init; }

    public Action<ZoneTransferEvent>? OnZoneTransferApplied { get; init; }
}
