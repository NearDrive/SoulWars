using Game.Core;

namespace Game.Server;

public readonly record struct ServerConfig(
    int Seed,
    int TickHz,
    int SnapshotEveryTicks,
    Fix32 VisionRadius,
    Fix32 VisionRadiusSq,
    int ZoneCount,
    int MapWidth,
    int MapHeight,
    int NpcCountPerZone,
    InvariantOptions Invariants)
{
    public SimulationConfig ToSimulationConfig()
    {
        SimulationConfig baseline = SimulationConfig.Default(Seed);

        return baseline with
        {
            Seed = Seed,
            TickHz = TickHz,
            ZoneCount = ZoneCount,
            MapWidth = MapWidth,
            MapHeight = MapHeight,
            NpcCountPerZone = NpcCountPerZone,
            Invariants = Invariants
        };
    }

    public static ServerConfig Default(int seed = 123) => new(
        Seed: seed,
        TickHz: 20,
        SnapshotEveryTicks: 1,
        VisionRadius: Fix32.FromInt(12),
        VisionRadiusSq: Fix32.FromInt(12) * Fix32.FromInt(12),
        ZoneCount: 1,
        MapWidth: 32,
        MapHeight: 32,
        NpcCountPerZone: 0,
        Invariants: InvariantOptions.Enabled);
}
