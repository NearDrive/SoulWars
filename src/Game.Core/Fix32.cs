namespace Game.Core;

public readonly record struct Fix32(int Raw) : IComparable<Fix32>
{
    public const int FractionalBits = 16;
    public const int OneRaw = 1 << FractionalBits;

    public static readonly Fix32 Zero = new(0);
    public static readonly Fix32 One = new(OneRaw);

    public static Fix32 FromInt(int value) => new(checked(value << FractionalBits));

    public static Fix32 FromFloat(float value) => new((int)(value * OneRaw));

    public static Fix32 Abs(Fix32 value) => new(Math.Abs(value.Raw));

    public static Fix32 Clamp(Fix32 value, Fix32 min, Fix32 max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    public static int FloorToInt(Fix32 value) => value.Raw >> FractionalBits;

    public int CompareTo(Fix32 other) => Raw.CompareTo(other.Raw);

    public static bool operator <(Fix32 left, Fix32 right) => left.Raw < right.Raw;
    public static bool operator >(Fix32 left, Fix32 right) => left.Raw > right.Raw;
    public static bool operator <=(Fix32 left, Fix32 right) => left.Raw <= right.Raw;
    public static bool operator >=(Fix32 left, Fix32 right) => left.Raw >= right.Raw;

    public static Fix32 operator +(Fix32 left, Fix32 right) => new(checked(left.Raw + right.Raw));
    public static Fix32 operator -(Fix32 left, Fix32 right) => new(checked(left.Raw - right.Raw));
    public static Fix32 operator -(Fix32 value) => new(checked(-value.Raw));

    public static Fix32 operator *(Fix32 left, Fix32 right)
    {
        long raw = ((long)left.Raw * right.Raw) >> FractionalBits;
        return new((int)raw);
    }

    public static Fix32 operator /(Fix32 left, Fix32 right)
    {
        if (right.Raw == 0)
        {
            throw new DivideByZeroException();
        }

        long raw = ((long)left.Raw << FractionalBits) / right.Raw;
        return new((int)raw);
    }

    public override string ToString() => (Raw / (float)OneRaw).ToString("0.#####");
}
