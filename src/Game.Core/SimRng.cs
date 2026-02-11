namespace Game.Core;

public sealed class SimRng
{
    private uint _state;

    public SimRng(int seed)
    {
        _state = unchecked((uint)seed);

        if (_state == 0)
        {
            _state = 0x6D2B79F5u;
        }
    }

    public uint NextU32()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;

        _state = x;
        return x;
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
        }

        uint range = (uint)(maxExclusive - minInclusive);
        uint value = NextU32() % range;

        return (int)value + minInclusive;
    }
}
