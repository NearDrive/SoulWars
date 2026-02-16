using Microsoft.Extensions.Logging;

namespace Game.Server;

public static class ServerLogEvents
{
    public static readonly EventId ServerStarted = new(1000, nameof(ServerStarted));
    public static readonly EventId SessionConnected = new(1001, nameof(SessionConnected));
    public static readonly EventId SessionEnteredZone = new(1002, nameof(SessionEnteredZone));
    public static readonly EventId SessionDisconnected = new(1003, nameof(SessionDisconnected));
    public static readonly EventId ProtocolDecodeFailed = new(1100, nameof(ProtocolDecodeFailed));
    public static readonly EventId OversizedMessage = new(1101, nameof(OversizedMessage));
    public static readonly EventId UnknownClientMessageType = new(1102, nameof(UnknownClientMessageType));
    public static readonly EventId SnapshotEmitted = new(1004, nameof(SnapshotEmitted));
    public static readonly EventId SnapshotResent = new(1005, nameof(SnapshotResent));
    public static readonly EventId InvalidClientAck = new(1103, nameof(InvalidClientAck));
    public static readonly EventId AbuseDisconnect = new(1104, nameof(AbuseDisconnect));
    public static readonly EventId UnhandledException = new(1200, nameof(UnhandledException));
}
