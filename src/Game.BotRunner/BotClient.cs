using Game.Protocol;

namespace Game.BotRunner;

public sealed class BotClient : IAsyncDisposable
{
    private readonly HeadlessClient _client = new();

    public BotClient(int botIndex, int zoneId)
    {
        if (botIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(botIndex));
        }

        BotIndex = botIndex;
        ZoneId = zoneId;
    }

    public int BotIndex { get; }

    public SessionId? SessionId { get; private set; }

    public int? EntityId { get; private set; }

    public int ZoneId { get; }

    public int SnapshotsReceived { get; private set; }

    public int LastSnapshotTick { get; private set; }

    public async Task ConnectAndEnterAsync(string host, int port, CancellationToken ct)
    {
        await _client.ConnectAsync(host, port, ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            DrainMessages(_ => { });

            if (SessionId is not null)
            {
                break;
            }

            await Task.Yield();
        }

        _client.EnterZone(ZoneId);

        while (!ct.IsCancellationRequested)
        {
            DrainMessages(_ => { });

            if (EntityId is not null)
            {
                return;
            }

            await Task.Yield();
        }

        ct.ThrowIfCancellationRequested();
    }

    public void SendInput(int tick, sbyte mx, sbyte my)
    {
        if (tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        _client.SendInput(tick, mx, my);
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
                    break;
            }

            onMsg(message);
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
