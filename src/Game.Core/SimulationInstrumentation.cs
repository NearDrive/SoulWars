namespace Game.Core;

public sealed class SimulationInstrumentation
{
    public Action<int>? CountEntitiesVisited { get; init; }

    public Action<int>? CountCollisionChecks { get; init; }
}
