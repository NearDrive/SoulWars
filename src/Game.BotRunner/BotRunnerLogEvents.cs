using Microsoft.Extensions.Logging;

namespace Game.BotRunner;

public static class BotRunnerLogEvents
{
    public static readonly EventId ScenarioStart = new(2000, nameof(ScenarioStart));
    public static readonly EventId ScenarioEnd = new(2001, nameof(ScenarioEnd));
    public static readonly EventId BotSummary = new(2002, nameof(BotSummary));
    public static readonly EventId InvariantFailed = new(2100, nameof(InvariantFailed));
}
