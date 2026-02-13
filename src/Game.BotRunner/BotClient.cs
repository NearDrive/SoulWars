using System.Linq;
using Game.Protocol;
using Game.Server;

namespace Game.BotRunner;

public sealed class BotClient : IAsyncDisposable
{
    private readonly HeadlessClient _client;

    public BotClient(int botIndex, int zoneId, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
        : this(botIndex, zoneId, endpoint: null, loggerFactory)
    {
    }

    public BotClient(int botIndex, int zoneId, IClientEndpoint? endpoint, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
    {
        if (botIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(botIndex));
        }

        BotIndex = botIndex;
        ZoneId = zoneId;
        AccountId = $"bot-{botIndex:000}";
        _client = endpoint is null ? new HeadlessClient(loggerFactory) : new HeadlessClient(endpoint, loggerFactory);
    }

    public int BotIndex { get; }

    public string AccountId { get; }

    public SessionId? SessionId { get; private set; }

    public int? EntityId { get; private set; }

    public int ZoneId { get; }

    public int SnapshotsReceived { get; private set; }

    public int LastSnapshotTick { get; private set; }

    public Snapshot? LastSnapshot { get; private set; }

    public int? SelfHp { get; private set; }

    private bool _enterZoneSent;
    private bool _helloSent;

    public bool HasWelcome => SessionId is not null;

    public bool HasEntered => EntityId is not null;

    public bool HandshakeDone => HasWelcome && HasEntered;

    public Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        return _client.ConnectAsync(host, port, ct);
    }


    public void SendHelloIfNeeded()
    {
        if (HasWelcome || _helloSent)
        {
            return;
        }

        _client.SendHello(AccountId, "bot-client");
        _helloSent = true;
    }

    public void EnterZone()
    {
        if (_enterZoneSent || !HasWelcome)
        {
            return;
        }

        _client.EnterZone(ZoneId);
        _enterZoneSent = true;
    }

    public void PumpMessages(Action<IServerMessage> onMsg)
    {
        DrainMessages(onMsg);
    }

    public void SendInput(int tick, sbyte mx, sbyte my)
    {
        if (tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        _client.SendInput(tick, mx, my);
    }

    public void SendAttackIntent(int tick, int attackerId, int targetId)
    {
        if (tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        _client.SendAttackIntent(tick, attackerId, targetId, ZoneId);
    }

    public void DrainMessages(Action<IServerMessage> onMsg)
    {
        ArgumentNullException.ThrowIfNull(onMsg);

        while (_client.TryRead(out IServerMessage? message))
        {
            if (message is null)
            {
                continue;
            }

            switch (message)
            {
                case Welcome welcome:
                    SessionId = welcome.SessionId;
                    break;
                case EnterZoneAck ack:
                    EntityId = ack.EntityId;
                    break;
                case Snapshot snapshot:
                    SnapshotsReceived++;
                    LastSnapshotTick = Math.Max(LastSnapshotTick, snapshot.Tick);
                    LastSnapshot = snapshot;
                    if (EntityId is int selfId)
                    {
                        SnapshotEntity? self = snapshot.Entities.FirstOrDefault(entity => entity.EntityId == selfId);
                        if (self is not null)
                        {
                            SelfHp = self.Hp;
                        }
                    }

                    break;
            }

            onMsg(message);
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
