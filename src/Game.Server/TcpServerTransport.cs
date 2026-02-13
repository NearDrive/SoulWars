using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Game.Server;

public sealed class TcpServerTransport : IAsyncDisposable
{
    private readonly ConcurrentQueue<IServerEndpoint> _accepted = new();
    private readonly List<TcpEndpoint> _allEndpoints = new();
    private readonly object _gate = new();
    private readonly ILogger<TcpServerTransport> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public TcpServerTransport(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<TcpServerTransport>();
    }

    public int BoundPort { get; private set; }

    public Task StartAsync(IPAddress ip, int port, CancellationToken ct)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("Transport already started.");
        }

        _listener = new TcpListener(ip, port);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    public IReadOnlyList<IServerEndpoint> DrainAcceptedEndpoints()
    {
        List<IServerEndpoint> endpoints = new();
        while (_accepted.TryDequeue(out IServerEndpoint? endpoint))
        {
            endpoints.Add(endpoint);
        }

        return endpoints;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        if (_listener is not null)
        {
            _listener.Stop();
        }

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        List<TcpEndpoint> endpoints;
        lock (_gate)
        {
            endpoints = _allEndpoints.ToList();
            _allEndpoints.Clear();
        }

        foreach (TcpEndpoint endpoint in endpoints)
        {
            await endpoint.DisposeAsync().ConfigureAwait(false);
        }

        _cts?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        TcpListener listener = _listener ?? throw new InvalidOperationException("Listener is not initialized.");

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ServerLogEvents.UnhandledException, ex, "UnhandledException");
                break;
            }

            TcpEndpoint endpoint = new(client, _loggerFactory.CreateLogger<TcpEndpoint>());
            lock (_gate)
            {
                _allEndpoints.Add(endpoint);
            }

            _accepted.Enqueue(endpoint);
        }
    }
}
