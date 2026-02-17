using Game.Core;

namespace Game.Server;

public readonly record struct ServerConfig(
    int Seed,
    int TickHz,
    int SnapshotEveryTicks,
    Fix32 VisionRadius,
    Fix32 VisionRadiusSq,
    int ZoneCount,
    int MapWidth,
    int MapHeight,
    int NpcCountPerZone,
    int DisconnectGraceTicks,
    int MaxPayloadBytes,
    int MaxConcurrentSessions,
    int MaxConnectionsPerIp,
    int MaxInputsPerTickPerSession,
    int MaxMsgsPerTick,
    int MaxBytesPerTick,
    int AbuseStrikesToDeny,
    int AbuseWindowTicks,
    int DenyTicks,
    int SnapshotRetryLimit,
    Fix32 MaxMoveSpeed,
    Fix32 MaxMoveVectorLen,
    InvariantOptions Invariants,
    bool EnableStructuredLogs,
    bool EnableMetrics,
    string? ZoneDefinitionsPath,
    string? VendorDefinitionsPath,
    bool EnableTickReports)
{
    public Fix32 AoiRadius => VisionRadius;

    public Fix32 AoiRadiusSq => VisionRadiusSq;

    public SimulationConfig ToSimulationConfig()
    {
        SimulationConfig baseline = SimulationConfig.Default(Seed);

        return baseline with
        {
            Seed = Seed,
            TickHz = TickHz,
            ZoneCount = ZoneCount,
            MapWidth = MapWidth,
            MapHeight = MapHeight,
            NpcCountPerZone = NpcCountPerZone,
            Invariants = Invariants
        };
    }

    public static ServerConfig Default(int seed = 123) => new(
        Seed: seed,
        TickHz: 20,
        SnapshotEveryTicks: 1,
        VisionRadius: Fix32.FromInt(12),
        VisionRadiusSq: Fix32.FromInt(12) * Fix32.FromInt(12),
        ZoneCount: 1,
        MapWidth: 32,
        MapHeight: 32,
        NpcCountPerZone: 0,
        DisconnectGraceTicks: 300,
        MaxPayloadBytes: 64 * 1024,
        MaxConcurrentSessions: 256,
        MaxConnectionsPerIp: 8,
        MaxInputsPerTickPerSession: 8,
        MaxMsgsPerTick: 1024,
        MaxBytesPerTick: 512_000,
        AbuseStrikesToDeny: 3,
        AbuseWindowTicks: 10_000,
        DenyTicks: 10_000,
        SnapshotRetryLimit: 1024,
        MaxMoveSpeed: Fix32.FromInt(4),
        MaxMoveVectorLen: Fix32.One,
        Invariants: InvariantOptions.Enabled,
        EnableStructuredLogs: false,
        EnableMetrics: true,
        ZoneDefinitionsPath: null,
        VendorDefinitionsPath: null,
        EnableTickReports: false);
}
