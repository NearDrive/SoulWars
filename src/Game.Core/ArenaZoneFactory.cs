using System.Collections.Immutable;

namespace Game.Core;

public static class ArenaZoneFactory
{
    private static readonly ImmutableArray<Vec2Fix> PlayerSpawnPoints = ImmutableArray.Create(
        TileCenter(4, 4),
        TileCenter(27, 27));

    public static int ArenaZoneId => 1;

    public static WorldState CreateWorld(SimulationConfig config)
    {
        ZoneDefinitions definitions = CreateDefinitions(config);
        return Simulation.CreateInitialState(config with { ZoneCount = 1, NpcCountPerZone = 0 }, definitions);
    }

    public static Vec2Fix ResolvePlayerSpawnPoint(int playerId)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }

        int index = (playerId - 1) % PlayerSpawnPoints.Length;
        return PlayerSpawnPoints[index];
    }

    private static ZoneDefinitions CreateDefinitions(SimulationConfig config)
    {
        ZoneDefinition arena = new(
            ZoneId: new ZoneId(ArenaZoneId),
            Bounds: new ZoneBounds(
                MinX: Fix32.FromInt(1),
                MinY: Fix32.FromInt(1),
                MaxX: Fix32.FromInt(config.MapWidth - 1),
                MaxY: Fix32.FromInt(config.MapHeight - 1)),
            StaticObstacles: ImmutableArray.Create(
                new ZoneAabb(TileCenter(16, 16), new Vec2Fix(Fix32.FromInt(1), Fix32.FromInt(5))),
                new ZoneAabb(TileCenter(10, 16), new Vec2Fix(Fix32.FromInt(1), Fix32.FromInt(1))),
                new ZoneAabb(TileCenter(22, 16), new Vec2Fix(Fix32.FromInt(1), Fix32.FromInt(1)))),
            NpcSpawns: ImmutableArray.Create(
                new NpcSpawnDefinition(
                    NpcArchetypeId: "arena.guard",
                    Count: 3,
                    Level: 1,
                    SpawnPoints: ImmutableArray.Create(
                        TileCenter(6, 5),
                        TileCenter(8, 22),
                        TileCenter(25, 10)))),
            LootRules: null,
            RespawnPoint: PlayerSpawnPoints[0]);

        return new ZoneDefinitions(ImmutableArray.Create(arena));
    }

    private static Vec2Fix TileCenter(int x, int y)
    {
        Fix32 half = new(Fix32.OneRaw / 2);
        return new Vec2Fix(Fix32.FromInt(x) + half, Fix32.FromInt(y) + half);
    }
}
