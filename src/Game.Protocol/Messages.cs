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
    PayloadTooLarge = 3
}

public static class ProtocolConstants
{
    public const int CurrentProtocolVersion = 1;

    public static readonly string[] ServerCapabilities =
    {
        "mvp4-proto-v1",
        "snapshots",
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

public sealed record TeleportRequest(int ToZoneId) : IClientMessage;

public sealed record Welcome(
    SessionId SessionId,
    PlayerId PlayerId,
    int ProtocolVersion = ProtocolConstants.CurrentProtocolVersion,
    string[]? ServerCapabilities = null) : IServerMessage;

public sealed record Disconnect(DisconnectReason Reason) : IServerMessage;

public sealed record EnterZoneAck(int ZoneId, int EntityId) : IServerMessage;

public sealed record Snapshot(int Tick, int ZoneId, SnapshotEntity[] Entities) : IServerMessage;

public sealed record SnapshotV2(int Tick, int ZoneId, SnapshotEntity[] Entities) : IServerMessage;

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
