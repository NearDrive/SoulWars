using System.Collections.Immutable;

namespace Game.Core;

public enum TileKind : byte
{
    Empty = 0,
    Solid = 1
}

public sealed record TileMap(int Width, int Height, ImmutableArray<TileKind> Tiles)
{
    public TileKind Get(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return TileKind.Solid;
        }

        int index = (y * Width) + x;
        return Tiles[index];
    }
}

public static class WorldGen
{
    public static TileMap Generate(SimulationConfig config, int width, int height)
    {
        SimRng rng = new(config.Seed);
        ImmutableArray<TileKind>.Builder builder = ImmutableArray.CreateBuilder<TileKind>(width * height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool keepSpawnAreaOpen = x >= 1 && x <= 4 && y >= 1 && y <= 4;

                if (border)
                {
                    builder.Add(TileKind.Solid);
                    continue;
                }

                if (keepSpawnAreaOpen)
                {
                    builder.Add(TileKind.Empty);
                    continue;
                }

                int roll = rng.NextInt(0, 100);
                builder.Add(roll < 7 ? TileKind.Solid : TileKind.Empty);
            }
        }

        return new TileMap(width, height, builder.MoveToImmutable());
    }
}
