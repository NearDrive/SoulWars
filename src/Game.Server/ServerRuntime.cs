using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Game.Server;

public sealed class ServerRuntime : IAsyncDisposable
{
    private readonly ILogger<ServerRuntime> _logger;
    private readonly TcpServerTransport _transport;

    public ServerRuntime(ILoggerFactory? loggerFactory = null)
    {
        LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = LoggerFactory.CreateLogger<ServerRuntime>();
        _transport = new TcpServerTransport(LoggerFactory);
    }

    public ILoggerFactory LoggerFactory { get; }

    public ServerHost Host { get; private set; } = null!;

    public int BoundPort => _transport.BoundPort;

    public async Task StartAsync(ServerConfig cfg, IPAddress ip, int port, CancellationToken ct, ServerBootstrap? bootstrap = null)
    {
        Host = new ServerHost(cfg, LoggerFactory, bootstrap: bootstrap);
        await _transport.StartAsync(ip, port, ct).ConfigureAwait(false);
        _logger.LogInformation(
            ServerLogEvents.ServerStarted,
            "ServerStarted seed={Seed} tickHz={TickHz} snapshotEvery={SnapshotEvery}",
            cfg.Seed,
            cfg.TickHz,
            cfg.SnapshotEveryTicks);
    }

    public void StepOnce()
    {
        long start = Stopwatch.GetTimestamp();
        PumpTransportOnce();
        Host.ProcessInboundOnce();
        Host.AdvanceSimulationOnce();
        Host.Metrics.RecordTick(Stopwatch.GetTimestamp() - start);
    }

    public void ProcessInboundOnce()
    {
        Host.ProcessInboundOnce();
    }

    public void PumpTransportOnce()
    {
        AttachAcceptedEndpoints();
    }

    public void AdvanceTicks(int n)
    {
        for (int i = 0; i < n; i++)
        {
            StepOnce();
        }
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();

    private void AttachAcceptedEndpoints()
    {
        foreach (IServerEndpoint endpoint in _transport.DrainAcceptedEndpoints())
        {
            Host.Connect(endpoint);
        }
    }
}
