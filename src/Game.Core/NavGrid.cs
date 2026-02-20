namespace Game.Core;

public readonly record struct TileCoord(int X, int Y)
{
    public int ToNodeId(int width) => (Y * width) + X;

    public static TileCoord FromNodeId(int nodeId, int width)
        => new(nodeId % width, nodeId / width);
}

public sealed class NavGrid
{
    private readonly bool[] walkable;

    public NavGrid(int width, int height, bool[] walkable)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(walkable);

        if (walkable.Length != width * height)
        {
            throw new ArgumentException("Walkable length must match width * height.", nameof(walkable));
        }

        Width = width;
        Height = height;
        this.walkable = walkable;
    }

    public int Width { get; }

    public int Height { get; }

    public int NodeCount => Width * Height;

    public static NavGrid FromTileMap(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        bool[] walkable = new bool[map.Width * map.Height];
        int index = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                walkable[index++] = map.Get(x, y) == TileKind.Empty;
            }
        }

        return new NavGrid(map.Width, map.Height, walkable);
    }

    public bool IsInBounds(TileCoord tile)
        => tile.X >= 0 && tile.Y >= 0 && tile.X < Width && tile.Y < Height;

    public bool IsWalkable(TileCoord tile)
        => IsInBounds(tile) && walkable[tile.ToNodeId(Width)];

    public bool IsWalkableNode(int nodeId)
        => nodeId >= 0 && nodeId < walkable.Length && walkable[nodeId];
}
