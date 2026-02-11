namespace Game.Core;

public readonly record struct Vec2Fix(Fix32 X, Fix32 Y)
{
    public static readonly Vec2Fix Zero = new(Fix32.Zero, Fix32.Zero);

    public static Vec2Fix operator +(Vec2Fix left, Vec2Fix right) => new(left.X + right.X, left.Y + right.Y);
    public static Vec2Fix operator -(Vec2Fix left, Vec2Fix right) => new(left.X - right.X, left.Y - right.Y);

    public Fix32 LengthSq() => (X * X) + (Y * Y);
}
