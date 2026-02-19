using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class LineOfSightTests
{
    [Fact]
    public void LoS_StraightLine_NoObstacles_True()
    {
        FakeCollisionGrid collision = new(8, 8);

        bool visible = LineOfSight.HasLineOfSight(collision, Fix32.FromInt(1), Fix32.FromInt(1), Fix32.FromInt(5), Fix32.FromInt(1));

        Assert.True(visible);
    }

    [Fact]
    public void LoS_StraightLine_ObstacleInBetween_False()
    {
        FakeCollisionGrid collision = new(8, 8);
        collision.SetBlocked(3, 1);

        bool visible = LineOfSight.HasLineOfSight(collision, Fix32.FromInt(1), Fix32.FromInt(1), Fix32.FromInt(5), Fix32.FromInt(1));

        Assert.False(visible);
    }

    [Fact]
    public void LoS_Diagonal_NoObstacles_True()
    {
        FakeCollisionGrid collision = new(8, 8);

        bool visible = LineOfSight.HasLineOfSight(collision, Fix32.FromInt(1), Fix32.FromInt(1), Fix32.FromInt(5), Fix32.FromInt(5));

        Assert.True(visible);
    }

    [Fact]
    public void LoS_Diagonal_ObstacleOnPath_False()
    {
        FakeCollisionGrid collision = new(8, 8);
        collision.SetBlocked(3, 3);

        bool visible = LineOfSight.HasLineOfSight(collision, Fix32.FromInt(1), Fix32.FromInt(1), Fix32.FromInt(5), Fix32.FromInt(5));

        Assert.False(visible);
    }

    [Fact]
    public void LoS_EndpointTargetTileBlocked_False()
    {
        FakeCollisionGrid collision = new(8, 8);
        collision.SetBlocked(5, 1);

        bool visible = LineOfSight.HasLineOfSight(collision, Fix32.FromInt(1), Fix32.FromInt(1), Fix32.FromInt(5), Fix32.FromInt(1));

        Assert.False(visible);
    }

    [Fact]
    public void LoS_IgnoresStartTile_WhenBlocked_StillTrue()
    {
        FakeCollisionGrid collision = new(8, 8);
        collision.SetBlocked(1, 1);

        bool visible = LineOfSight.HasLineOfSight(collision, Fix32.FromInt(1), Fix32.FromInt(1), Fix32.FromInt(5), Fix32.FromInt(1));

        Assert.True(visible);
    }

    [Fact]
    public void LoS_DiagonalCornerCrossing_ChecksAdjacentTiles_False()
    {
        FakeCollisionGrid collision = new(8, 8);
        collision.SetBlocked(2, 1);

        bool visible = LineOfSight.HasLineOfSight(collision, Fix32.FromInt(1), Fix32.FromInt(1), Fix32.FromInt(3), Fix32.FromInt(3));

        Assert.False(visible);
    }

    private sealed class FakeCollisionGrid(int width, int height) : IZoneCollision
    {
        private readonly bool[,] blocked = new bool[width, height];

        public bool IsBlocked(int tileX, int tileY)
        {
            if (tileX < 0 || tileY < 0 || tileX >= width || tileY >= height)
            {
                return true;
            }

            return blocked[tileX, tileY];
        }

        public void SetBlocked(int tileX, int tileY)
        {
            blocked[tileX, tileY] = true;
        }
    }
}
