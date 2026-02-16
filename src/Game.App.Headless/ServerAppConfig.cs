namespace Game.App.Headless;

public sealed record ServerAppConfig(
    int Seed,
    int Port,
    string? SqlitePath,
    int ZoneCount,
    int BotCount,
    string? ZoneDefinitionsPath);
