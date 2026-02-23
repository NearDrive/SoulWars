namespace Game.Core;

public readonly record struct AiBudgetConfig(
    int MaxPathExpansionsPerTick,
    int MaxRepathsPerTick,
    int MaxAiDecisionsPerTick,
    bool IsConfigured = true)
{
    public static AiBudgetConfig Default => new(
        MaxPathExpansionsPerTick: 4096,
        MaxRepathsPerTick: 64,
        MaxAiDecisionsPerTick: 256);

    public AiBudgetConfig ClampNonNegative() => new(
        MaxPathExpansionsPerTick: Math.Max(0, MaxPathExpansionsPerTick),
        MaxRepathsPerTick: Math.Max(0, MaxRepathsPerTick),
        MaxAiDecisionsPerTick: Math.Max(0, MaxAiDecisionsPerTick),
        IsConfigured: IsConfigured);
}

public struct AiBudgetState
{
    public AiBudgetState(AiBudgetConfig config)
    {
        config = config.ClampNonNegative();
        RemainingPathExpansions = config.MaxPathExpansionsPerTick;
        RemainingRepaths = config.MaxRepathsPerTick;
        RemainingAiDecisions = config.MaxAiDecisionsPerTick;
        ConsumedPathExpansions = 0;
        ConsumedRepaths = 0;
        ConsumedAiDecisions = 0;
    }

    public int RemainingPathExpansions { get; private set; }
    public int RemainingRepaths { get; private set; }
    public int RemainingAiDecisions { get; private set; }

    public int ConsumedPathExpansions { get; private set; }
    public int ConsumedRepaths { get; private set; }
    public int ConsumedAiDecisions { get; private set; }

    public bool TryConsumeAiDecision()
    {
        if (RemainingAiDecisions <= 0)
        {
            return false;
        }

        RemainingAiDecisions--;
        ConsumedAiDecisions++;
        return true;
    }

    public bool TryConsumeRepathSlot()
    {
        if (RemainingRepaths <= 0)
        {
            return false;
        }

        RemainingRepaths--;
        ConsumedRepaths++;
        return true;
    }

    public int GetPathExpansionAllowance(int perCallCap)
    {
        if (perCallCap <= 0 || RemainingPathExpansions <= 0)
        {
            return 0;
        }

        return Math.Min(RemainingPathExpansions, perCallCap);
    }

    public void ConsumePathExpansions(int expandedCount)
    {
        if (expandedCount <= 0)
        {
            return;
        }

        int clamped = Math.Min(expandedCount, RemainingPathExpansions);
        RemainingPathExpansions -= clamped;
        ConsumedPathExpansions += clamped;
    }
}
