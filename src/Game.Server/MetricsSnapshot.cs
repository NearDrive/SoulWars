namespace Game.Server;

public sealed record MetricsSnapshot(
    int TickCount,
    double TickP50Ms,
    double TickP95Ms,
    double MessagesPerSecondIn,
    double MessagesPerSecondOut);
