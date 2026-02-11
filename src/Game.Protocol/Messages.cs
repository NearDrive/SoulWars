namespace Game.Protocol;

public readonly record struct SessionId(int Value);

public readonly record struct PlayerId(int Value);

public interface IClientMessage;

public interface IServerMessage;

public sealed record Hello(string ClientVersion) : IClientMessage;

public sealed record EnterZoneRequest(int ZoneId) : IClientMessage;

public sealed record InputCommand(int Tick, sbyte MoveX, sbyte MoveY) : IClientMessage;

public sealed record LeaveZoneRequest(int ZoneId) : IClientMessage;

public sealed record Welcome(SessionId SessionId) : IServerMessage;

public sealed record EnterZoneAck(int ZoneId, int EntityId) : IServerMessage;

public sealed record Snapshot(int Tick, int ZoneId, SnapshotEntity[] Entities) : IServerMessage;

public sealed record Error(string Code, string Message) : IServerMessage;

public sealed record SnapshotEntity(int EntityId, int PosXRaw, int PosYRaw, int VelXRaw = 0, int VelYRaw = 0);
