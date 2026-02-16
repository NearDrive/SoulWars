using Game.Protocol;
using Game.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Game.BotRunner;

public sealed class HeadlessClient : IAsyncDisposable
{
    private readonly TcpClientEndpoint? _tcpEndpoint;
    private readonly IClientEndpoint? _inMemoryEndpoint;
    private readonly ILogger<HeadlessClient> _logger;

    public HeadlessClient(ILoggerFactory? loggerFactory = null)
    {
        _tcpEndpoint = new TcpClientEndpoint();
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<HeadlessClient>();
    }

    public HeadlessClient(IClientEndpoint endpoint, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _inMemoryEndpoint = endpoint;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<HeadlessClient>();
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
            try
            {
                message = ProtocolCodec.DecodeServer(payload);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(BotRunnerLogEvents.InvariantFailed, ex, "InvariantFailed reason=server_decode_failed");
            }
        }

        message = null;
        return false;
    }


    public void SendHello(string accountId, string clientVersion = "headless-client")
    {
        Send(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, accountId));
    }

    public void EnterZone(int zoneId) => Send(new EnterZoneRequest(zoneId));

    public void SendInput(int tick, sbyte mx, sbyte my) => Send(new InputCommand(tick, mx, my));

    public void SendAttackIntent(int tick, int attackerId, int targetId, int zoneId) =>
        Send(new AttackIntent(tick, attackerId, targetId, zoneId));

    public void Teleport(int toZoneId) => Send(new TeleportRequest(toZoneId));

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
