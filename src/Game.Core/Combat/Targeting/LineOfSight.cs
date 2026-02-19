namespace Game.Core;

public interface IZoneCollision
{
    bool IsBlocked(int tileX, int tileY);
}

public static class LineOfSight
{
    // Deterministic policy:
    // - Ignore the start tile (caster position) to avoid self-blocking.
    // - Validate all subsequent tiles, including the target tile.
    // - On exact corner crossings, check both orthogonal adjacent tiles (conservative no-corner-cutting).
    public static bool HasLineOfSight(IZoneCollision collision, Fix32 ax, Fix32 ay, Fix32 bx, Fix32 by)
    {
        int startX = Fix32.FloorToInt(ax);
        int startY = Fix32.FloorToInt(ay);
        int targetX = Fix32.FloorToInt(bx);
        int targetY = Fix32.FloorToInt(by);

        int dx = targetX - startX;
        int dy = targetY - startY;
        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);
        int absDx = Math.Abs(dx);
        int absDy = Math.Abs(dy);

        int x = startX;
        int y = startY;
        int progressedX = 0;
        int progressedY = 0;
        bool firstTile = true;

        while (progressedX <= absDx && progressedY <= absDy)
        {
            if (!firstTile && collision.IsBlocked(x, y))
            {
                return false;
            }

            if (x == targetX && y == targetY)
            {
                return true;
            }

            int decision = ((1 + (2 * progressedX)) * absDy) - ((1 + (2 * progressedY)) * absDx);
            if (decision == 0)
            {
                int sideX = x + stepX;
                int sideY = y;
                int side2X = x;
                int side2Y = y + stepY;

                if (!(sideX == startX && sideY == startY) && collision.IsBlocked(sideX, sideY))
                {
                    return false;
                }

                if (!(side2X == startX && side2Y == startY) && collision.IsBlocked(side2X, side2Y))
                {
                    return false;
                }

                x += stepX;
                y += stepY;
                progressedX++;
                progressedY++;
            }
            else if (decision < 0)
            {
                x += stepX;
                progressedX++;
            }
            else
            {
                y += stepY;
                progressedY++;
            }

            firstTile = false;
        }

        return true;
    }

    public readonly struct TileMapCollision(TileMap map) : IZoneCollision
    {
        public bool IsBlocked(int tileX, int tileY)
        {
            return map.Get(tileX, tileY) == TileKind.Solid;
        }
    }
}
