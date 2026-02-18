namespace Game.Core;

using System.Collections.Immutable;

public readonly record struct SimulationConfig(
    int Seed,
    int TickHz,
    Fix32 DtFix,
    Fix32 MoveSpeed,
    Fix32 MaxSpeed,
    Fix32 Radius,
    int ZoneCount,
    int MapWidth,
    int MapHeight,
    int NpcCountPerZone,
    int NpcWanderPeriodTicks,
    Fix32 NpcAggroRange,
    InvariantOptions Invariants,
    ImmutableArray<SkillDefinition> SkillDefinitions = default)
{
    public static SimulationConfig Default(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277), // ~= 0.05
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(19661), // ~= 0.3
        ZoneCount: 1,
        MapWidth: 64,
        MapHeight: 64,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        SkillDefinitions: ImmutableArray<SkillDefinition>.Empty,
        Invariants: InvariantOptions.Enabled);
}
