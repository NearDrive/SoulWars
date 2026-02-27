namespace Game.Protocol;

public readonly record struct SessionId(int Value);

public readonly record struct PlayerId(int Value);

public interface IClientMessage;

public interface IServerMessage;

public enum DisconnectReason : byte
{
    Unknown = 0,
    VersionMismatch = 1,
    DecodeError = 2,
    PayloadTooLarge = 3,
    ConnLimitExceeded = 4,
    RateLimitExceeded = 5,
    DenyListed = 6,
    ProtocolViolation = 7
}

public static class ProtocolConstants
{
    public const int CurrentProtocolVersion = 1;

    public static readonly string[] ServerCapabilities =
    {
        "mvp4-proto-v1",
        "snapshots",
        "snapshot-seq-ack",
        "replay-verify"
    };
}

public sealed record HandshakeRequest(int ProtocolVersion, string AccountId) : IClientMessage;

public sealed record Hello(string ClientVersion) : IClientMessage;

public sealed record HelloV2(string ClientVersion, string AccountId) : IClientMessage;

public sealed record EnterZoneRequest(int ZoneId) : IClientMessage;

public sealed record EnterZoneRequestV2(int ZoneId) : IClientMessage;

public sealed record InputCommand(int Tick, sbyte MoveX, sbyte MoveY) : IClientMessage;

public sealed record AttackIntent(int Tick, int AttackerId, int TargetId, int ZoneId) : IClientMessage;

public sealed record LeaveZoneRequest(int ZoneId) : IClientMessage;

public sealed record LeaveZoneRequestV2(int ZoneId) : IClientMessage;

public sealed record ClientAckV2(int ZoneId, int LastSnapshotSeqReceived) : IClientMessage;

public sealed record TeleportRequest(int ToZoneId) : IClientMessage;

public sealed record LootIntent(int LootEntityId, int ZoneId) : IClientMessage;

public sealed record CastSkillCommand(
    int Tick,
    int CasterId,
    int SkillId,
    int ZoneId,
    byte TargetKind,
    int TargetEntityId,
    int TargetPosXRaw,
    int TargetPosYRaw) : IClientMessage;

public sealed record Welcome(
    SessionId SessionId,
    PlayerId PlayerId,
    int ProtocolVersion = ProtocolConstants.CurrentProtocolVersion,
    string[]? ServerCapabilities = null) : IServerMessage;

public sealed record Disconnect(DisconnectReason Reason) : IServerMessage;

public sealed record EnterZoneAck(int ZoneId, int EntityId) : IServerMessage;

public record Snapshot(int Tick, int ZoneId, SnapshotEntity[] Entities) : IServerMessage;

public sealed record SnapshotV2(
    int Tick,
    int ZoneId,
    int SnapshotSeq,
    bool IsFull,
    SnapshotEntity[] Entities,
    int[]? Leaves = null,
    SnapshotEntity[]? Enters = null,
    SnapshotEntity[]? Updates = null,
    ProjectileSnapshotV1[]? Projectiles = null,
    ProjectileEventV1[]? ProjectileEvents = null,
    HitEventV1[]? HitEvents = null)
    : Snapshot(Tick, ZoneId, Entities)
{
    public int[] Leaves { get; init; } = Leaves ?? Array.Empty<int>();

    public SnapshotEntity[] Enters { get; init; } = Enters ?? Array.Empty<SnapshotEntity>();

    public SnapshotEntity[] Updates { get; init; } = Updates ?? Array.Empty<SnapshotEntity>();

    public ProjectileSnapshotV1[] Projectiles { get; init; } = Projectiles ?? Array.Empty<ProjectileSnapshotV1>();

    public ProjectileEventV1[] ProjectileEvents { get; init; } = ProjectileEvents ?? Array.Empty<ProjectileEventV1>();

    public HitEventV1[] HitEvents { get; init; } = HitEvents ?? Array.Empty<HitEventV1>();
}

public sealed record ProjectileSnapshotV1(
    int ProjectileId,
    int SourceEntityId,
    int AbilityId,
    int PosXRaw,
    int PosYRaw,
    int VelXRaw,
    int VelYRaw,
    int RadiusRaw,
    int SpawnTick,
    int ExpireTick);

public sealed record ProjectileEventV1(
    int TickId,
    int ZoneId,
    int ProjectileId,
    byte Kind,
    int SourceEntityId,
    int TargetEntityId,
    int AbilityId,
    int PosXRaw,
    int PosYRaw);

public sealed record HitEventV1(
    int TickId,
    int ZoneId,
    int SourceEntityId,
    int TargetEntityId,
    int AbilityId,
    int HitPosXRaw,
    int HitPosYRaw,
    int EventSeq);

public sealed record Error(string Code, string Message) : IServerMessage;

public enum SnapshotEntityKind : byte
{
    Unknown = 0,
    Player = 1,
    Npc = 2
}

public sealed record SnapshotEntity(
    int EntityId,
    int PosXRaw,
    int PosYRaw,
    int VelXRaw = 0,
    int VelYRaw = 0,
    int Hp = 0,
    SnapshotEntityKind Kind = SnapshotEntityKind.Unknown);
