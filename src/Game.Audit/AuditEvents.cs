using Game.Core;

namespace Game.Audit;

public enum AuditEventType : byte
{
    PlayerConnected = 1,
    EnterZone = 2,
    Teleport = 3,
    Death = 4,
    DespawnDisconnected = 5
}

public readonly record struct AuditEventHeader(int Tick, int Seq, AuditEventType Type);

public readonly record struct AuditEvent(
    AuditEventHeader Header,
    string AccountId,
    int PlayerId,
    int ZoneId,
    int FromZoneId,
    int ToZoneId,
    int EntityId,
    int KillerEntityId)
{
    public static AuditEvent PlayerConnected(int tick, int seq, string accountId, int playerId)
        => new(new AuditEventHeader(tick, seq, AuditEventType.PlayerConnected), accountId, playerId, 0, 0, 0, 0, -1);

    public static AuditEvent EnterZone(int tick, int seq, int playerId, int zoneId, int entityId)
        => new(new AuditEventHeader(tick, seq, AuditEventType.EnterZone), string.Empty, playerId, zoneId, 0, 0, entityId, -1);

    public static AuditEvent Teleport(int tick, int seq, int playerId, int fromZoneId, int toZoneId, int entityId)
        => new(new AuditEventHeader(tick, seq, AuditEventType.Teleport), string.Empty, playerId, 0, fromZoneId, toZoneId, entityId, -1);

    public static AuditEvent Death(int tick, int seq, int entityId, int? killerEntityId)
        => new(new AuditEventHeader(tick, seq, AuditEventType.Death), string.Empty, 0, 0, 0, 0, entityId, killerEntityId ?? -1);

    public static AuditEvent DespawnDisconnected(int tick, int seq, int playerId, int entityId)
        => new(new AuditEventHeader(tick, seq, AuditEventType.DespawnDisconnected), string.Empty, playerId, 0, 0, 0, entityId, -1);

    public int? KillerEntityIdOrNull => KillerEntityId < 0 ? null : KillerEntityId;
}

public interface IAuditSink
{
    void Emit(AuditEvent evt);
}

public sealed class NullAuditSink : IAuditSink
{
    public static NullAuditSink Instance { get; } = new();

    private NullAuditSink()
    {
    }

    public void Emit(AuditEvent evt)
    {
    }
}

public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly List<AuditEvent> _events = new();

    public IReadOnlyList<AuditEvent> Events => _events;

    public void Emit(AuditEvent evt) => _events.Add(evt);

    public byte[] ToBytes()
    {
        using MemoryStream stream = new();
        using AuditLogWriter writer = new(stream);
        foreach (AuditEvent evt in _events)
        {
            writer.Append(in evt);
        }

        return stream.ToArray();
    }
}
