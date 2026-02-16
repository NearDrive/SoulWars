namespace Game.Core;

public static class Physics2D
{
    private static readonly Fix32 InvSqrt2 = new(46341);

    public static EntityState Integrate(
        EntityState entity,
        PlayerInput input,
        TileMap map,
        Fix32 dt,
        Fix32 moveSpeed,
        Fix32 radius,
        Action<int>? countCollisionChecks = null)
    {
        Vec2Fix desiredVelocity = ComputeDesiredVelocity(input, moveSpeed);
        Vec2Fix targetPosition = entity.Pos + new Vec2Fix(desiredVelocity.X * dt, desiredVelocity.Y * dt);

        Vec2Fix resolvedPosition = ResolveAxisSeparated(entity.Pos, targetPosition, radius, map, countCollisionChecks);

        Vec2Fix resolvedVelocity = new(
            dt.Raw == 0 ? Fix32.Zero : (resolvedPosition.X - entity.Pos.X) / dt,
            dt.Raw == 0 ? Fix32.Zero : (resolvedPosition.Y - entity.Pos.Y) / dt);

        return entity with
        {
            Pos = resolvedPosition,
            Vel = resolvedVelocity
        };
    }

    public static bool OverlapsSolidTile(Vec2Fix position, Fix32 radius, TileMap map, Action<int>? countCollisionChecks = null)
    {
        countCollisionChecks?.Invoke(1);
        Fix32 minX = position.X - radius;
        Fix32 maxX = position.X + radius;
        Fix32 minY = position.Y - radius;
        Fix32 maxY = position.Y + radius;

        int startX = Fix32.FloorToInt(minX);
        int endX = Fix32.FloorToInt(maxX);
        int startY = Fix32.FloorToInt(minY);
        int endY = Fix32.FloorToInt(maxY);

        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                if (map.Get(x, y) != TileKind.Solid)
                {
                    continue;
                }

                if (CircleOverlapsTile(position, radius, x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Vec2Fix ComputeDesiredVelocity(PlayerInput input, Fix32 moveSpeed)
    {
        Fix32 vx = Fix32.FromInt(input.MoveX) * moveSpeed;
        Fix32 vy = Fix32.FromInt(input.MoveY) * moveSpeed;

        if (input.MoveX != 0 && input.MoveY != 0)
        {
            vx *= InvSqrt2;
            vy *= InvSqrt2;
        }

        return new Vec2Fix(vx, vy);
    }

    private static Vec2Fix ResolveAxisSeparated(Vec2Fix current, Vec2Fix target, Fix32 radius, TileMap map, Action<int>? countCollisionChecks)
    {
        Vec2Fix afterX = new(target.X, current.Y);
        if (OverlapsSolidTile(afterX, radius, map, countCollisionChecks))
        {
            afterX = current;
        }

        Vec2Fix afterY = new(afterX.X, target.Y);
        if (OverlapsSolidTile(afterY, radius, map, countCollisionChecks))
        {
            afterY = afterX;
        }

        return afterY;
    }

    private static bool CircleOverlapsTile(Vec2Fix center, Fix32 radius, int tileX, int tileY)
    {
        Fix32 minX = Fix32.FromInt(tileX);
        Fix32 minY = Fix32.FromInt(tileY);
        Fix32 maxX = Fix32.FromInt(tileX + 1);
        Fix32 maxY = Fix32.FromInt(tileY + 1);

        Fix32 closestX = Fix32.Clamp(center.X, minX, maxX);
        Fix32 closestY = Fix32.Clamp(center.Y, minY, maxY);

        Fix32 dx = center.X - closestX;
        Fix32 dy = center.Y - closestY;
        Fix32 distSq = (dx * dx) + (dy * dy);

        return distSq <= (radius * radius);
    }
}
