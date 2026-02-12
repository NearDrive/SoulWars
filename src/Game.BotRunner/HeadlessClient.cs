using Game.Protocol;
using Game.Server;

namespace Game.BotRunner;

public sealed class HeadlessClient : IAsyncDisposable
{
    private readonly TcpClientEndpoint? _tcpEndpoint;
    private readonly IClientEndpoint? _inMemoryEndpoint;

    public HeadlessClient()
    {
        _tcpEndpoint = new TcpClientEndpoint();
    }

    public HeadlessClient(IClientEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _inMemoryEndpoint = endpoint;
    }

    public Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        if (_tcpEndpoint is null)
        {
            return Task.CompletedTask;
        }

        return _tcpEndpoint.ConnectAsync(host, port, ct);
    }

    public void Send(IClientMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        byte[] payload = ProtocolCodec.Encode(message);

        if (_tcpEndpoint is not null)
        {
            _tcpEndpoint.EnqueueToServer(payload);
            return;
        }

        _inMemoryEndpoint!.EnqueueToServer(payload);
    }

    public bool TryRead(out IServerMessage? message)
    {
        byte[] payload;
        bool hasPayload = _tcpEndpoint is not null
            ? _tcpEndpoint.TryDequeueFromServer(out payload)
            : _inMemoryEndpoint!.TryDequeueFromServer(out payload);

        if (hasPayload)
        {
            message = ProtocolCodec.DecodeServer(payload);
            return true;
        }

        message = null;
        return false;
    }

    public void EnterZone(int zoneId) => Send(new EnterZoneRequest(zoneId));

    public void SendInput(int tick, sbyte mx, sbyte my) => Send(new InputCommand(tick, mx, my));

    public async ValueTask DisposeAsync()
    {
        if (_tcpEndpoint is not null)
        {
            await _tcpEndpoint.DisposeAsync().ConfigureAwait(false);
            return;
        }

        _inMemoryEndpoint?.Close();
    }
}
