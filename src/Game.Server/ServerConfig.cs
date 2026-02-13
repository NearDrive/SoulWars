using Game.Core;

namespace Game.Server;

public readonly record struct ServerConfig(
    int Seed,
    int TickHz,
    int SnapshotEveryTicks,
    int ZoneCount,
    int MapWidth,
    int MapHeight,
    int NpcCountPerZone)
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
            NpcCountPerZone = NpcCountPerZone
        };
    }

    public static ServerConfig Default(int seed = 123) => new(
        Seed: seed,
        TickHz: 20,
        SnapshotEveryTicks: 1,
        ZoneCount: 1,
        MapWidth: 32,
        MapHeight: 32,
        NpcCountPerZone: 0);
}
