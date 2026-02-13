namespace Game.Core;

public readonly record struct SimulationConfig(
    int Seed,
    int TickHz,
    Fix32 DtFix,
    Fix32 MoveSpeed,
    Fix32 MaxSpeed,
    Fix32 Radius,
    int MapWidth,
    int MapHeight,
    int NpcCount,
    int NpcWanderPeriodTicks,
    Fix32 NpcAggroRange)
{
    public static SimulationConfig Default(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277), // ~= 0.05
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(19661), // ~= 0.3
        MapWidth: 64,
        MapHeight: 64,
        NpcCount: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6));
}
