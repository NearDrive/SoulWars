using Game.Core;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR69")]
public sealed class PathDeterminismTests
{
    [Fact]
    public void SameGridAndEndpoints_AlwaysReturnsSamePath()
    {
        NavGrid grid = PathfindingPr69TestHelpers.BuildGrid(
            ".......",
            ".###...",
            "...#...",
            "...#...",
            ".......");

        TileCoord start = new(0, 0);
        TileCoord goal = new(6, 4);

        DeterministicAStar finder = new();
        TileCoord[] baselineBuffer = new TileCoord[64];
        Assert.True(finder.TryFindPath(grid, start, goal, baselineBuffer, out int baselineLen, out _, maxExpandedNodes: 256));
        TileCoord[] baseline = baselineBuffer[..baselineLen];

        for (int i = 0; i < 20; i++)
        {
            TileCoord[] runBuffer = new TileCoord[64];
            Assert.True(finder.TryFindPath(grid, start, goal, runBuffer, out int runLen, out _, maxExpandedNodes: 256));
            Assert.Equal(baselineLen, runLen);
            Assert.Equal(baseline, runBuffer[..runLen]);
        }
    }
}

[Trait("Category", "PR69")]
public sealed class PathTieBreakerTests
{
    [Fact]
    public void EqualCostAlternatives_PicksStableRouteFromNeighborOrder()
    {
        NavGrid grid = PathfindingPr69TestHelpers.BuildGrid(
            ".....",
            ".....",
            ".....",
            ".....",
            ".....");

        TileCoord start = new(1, 1);
        TileCoord goal = new(3, 3);

        DeterministicAStar finder = new();
        TileCoord[] buffer = new TileCoord[16];

        Assert.True(finder.TryFindPath(grid, start, goal, buffer, out int len, out _, maxExpandedNodes: 64));

        TileCoord[] expected =
        [
            new TileCoord(1, 1),
            new TileCoord(2, 1),
            new TileCoord(3, 1),
            new TileCoord(3, 2),
            new TileCoord(3, 3)
        ];

        Assert.Equal(expected.Length, len);
        Assert.Equal(expected, buffer[..len]);
    }
}

[Trait("Category", "PR69")]
public sealed class PathBudgetLimitPr69Tests
{
    [Fact]
    public void InsufficientBudget_ReturnsFalseDeterministically()
    {
        NavGrid grid = PathfindingPr69TestHelpers.BuildGrid(
            ".....",
            ".....",
            ".....",
            ".....",
            ".....");

        DeterministicAStar finder = new();
        TileCoord[] buffer = new TileCoord[32];

        for (int i = 0; i < 10; i++)
        {
            bool found = finder.TryFindPath(
                grid,
                new TileCoord(0, 0),
                new TileCoord(4, 4),
                buffer,
                out int len,
                out _,
                maxExpandedNodes: 1);

            Assert.False(found);
            Assert.Equal(0, len);
        }
    }
}

file static class PathfindingPr69TestHelpers
{
    public static NavGrid BuildGrid(params string[] rows)
    {
        int height = rows.Length;
        int width = rows[0].Length;
        bool[] walkable = new bool[width * height];

        int index = 0;
        for (int y = 0; y < height; y++)
        {
            string row = rows[y];
            for (int x = 0; x < width; x++)
            {
                walkable[index++] = row[x] != '#';
            }
        }

        return new NavGrid(width, height, walkable);
    }
}
