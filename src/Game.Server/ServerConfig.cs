using Game.Core;

namespace Game.Server;

public readonly record struct ServerConfig(
    int Seed,
    int TickHz,
    int SnapshotEveryTicks,
    int MapWidth,
    int MapHeight,
    int NpcCount)
{
    public SimulationConfig ToSimulationConfig()
    {
        SimulationConfig baseline = SimulationConfig.Default(Seed);

        return baseline with
        {
            Seed = Seed,
            TickHz = TickHz,
            MapWidth = MapWidth,
            MapHeight = MapHeight,
            NpcCount = NpcCount
        };
    }

    public static ServerConfig Default(int seed = 123) => new(
        Seed: seed,
        TickHz: 20,
        SnapshotEveryTicks: 1,
        MapWidth: 32,
        MapHeight: 32,
        NpcCount: 0);
}
