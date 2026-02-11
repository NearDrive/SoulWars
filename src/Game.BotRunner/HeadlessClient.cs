using Game.Protocol;

namespace Game.BotRunner;

public sealed class HeadlessClient : IAsyncDisposable
{
    private readonly TcpClientEndpoint _endpoint = new();

    public Task ConnectAsync(string host, int port, CancellationToken ct) => _endpoint.ConnectAsync(host, port, ct);

    public void Send(IClientMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _endpoint.EnqueueToServer(ProtocolCodec.Encode(message));
    }

    public bool TryRead(out IServerMessage? message)
    {
        if (_endpoint.TryDequeueFromServer(out byte[] payload))
        {
            message = ProtocolCodec.DecodeServer(payload);
            return true;
        }

        message = null;
        return false;
    }

    public void EnterZone(int zoneId) => Send(new EnterZoneRequest(zoneId));

    public void SendInput(int tick, sbyte mx, sbyte my) => Send(new InputCommand(tick, mx, my));

    public ValueTask DisposeAsync() => _endpoint.DisposeAsync();
}
