using Game.Core;

namespace Game.BotRunner;

public sealed class BotAgent
{
    private readonly SimRng _rng;

    public BotAgent(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Config = config;
        _rng = new SimRng(config.InputSeed);
    }

    public BotConfig Config { get; }

    public (sbyte mx, sbyte my) GetMoveForTick(int tick)
    {
        if (tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        if (tick % 8 == 1)
        {
            return NextActiveDirection();
        }

        int axisBias = _rng.NextInt(0, 10);
        if (axisBias < 2)
        {
            return (0, 0);
        }

        return NextActiveDirection();
    }

    private (sbyte mx, sbyte my) NextActiveDirection()
    {
        while (true)
        {
            sbyte mx = (sbyte)_rng.NextInt(-1, 2);
            sbyte my = (sbyte)_rng.NextInt(-1, 2);
            if (mx != 0 || my != 0)
            {
                return (mx, my);
            }
        }
    }
}
