using System.Net;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Game.Server;

public sealed class ServerRuntime : IAsyncDisposable
{
    private readonly ILogger<ServerRuntime> _logger;
    private readonly TcpServerTransport _transport;
    private readonly ZoneManager<ServerHost> _zoneManager = new();

    public ServerRuntime(ILoggerFactory? loggerFactory = null)
    {
        LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = LoggerFactory.CreateLogger<ServerRuntime>();
        _transport = new TcpServerTransport(LoggerFactory);
    }

    public ILoggerFactory LoggerFactory { get; }

    public ServerHost Host { get; private set; } = null!;

    public IReadOnlyCollection<int> ZoneIds => _zoneManager.ZoneIds;

    public Action<int>? ZoneTickTraceSink { get; set; }

    public int BoundPort => _transport.BoundPort;

    public async Task StartAsync(ServerConfig cfg, IPAddress ip, int port, CancellationToken ct, ServerBootstrap? bootstrap = null)
    {
        Host = new ServerHost(cfg, LoggerFactory, metrics: new ServerMetrics(cfg.EnableMetrics), bootstrap: bootstrap);
        ConfigureSingleZoneHost(Host);
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
        PumpTransportOnce();
        foreach (KeyValuePair<int, ServerHost> zone in _zoneManager.OrderedEntries())
        {
            ZoneTickTraceSink?.Invoke(zone.Key);
            zone.Value.StepOnce();
        }
    }

    public void ProcessInboundOnce()
    {
        foreach (KeyValuePair<int, ServerHost> zone in _zoneManager.OrderedEntries())
        {
            zone.Value.ProcessInboundOnce();
        }
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

    public void ConfigureZones(IEnumerable<KeyValuePair<int, ServerHost>> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);

        ZoneManager<ServerHost> next = new();
        foreach ((int zoneId, ServerHost zoneHost) in zones)
        {
            next.Add(zoneId, zoneHost);
        }

        if (next.ZoneIds.Count == 0)
        {
            throw new InvalidOperationException("At least one zone host is required.");
        }

        _zoneManager.ClearAndCopyFrom(next);
        Host = _zoneManager.Get(_zoneManager.ZoneIds.Min());
    }

    public string ComputeManagedWorldChecksum()
    {
        StringBuilder builder = new();
        foreach (KeyValuePair<int, ServerHost> zone in _zoneManager.OrderedEntries())
        {
            builder.Append(zone.Key);
            builder.Append(':');
            builder.Append(StateChecksum.Compute(zone.Value.CurrentWorld));
            builder.Append(';');
        }

        byte[] payload = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private void AttachAcceptedEndpoints()
    {
        foreach (IServerEndpoint endpoint in _transport.DrainAcceptedEndpoints())
        {
            Host.TryConnect(endpoint, out _, out _);
        }
    }

    private void ConfigureSingleZoneHost(ServerHost host)
    {
        _zoneManager.Clear();
        _zoneManager.Add(1, host);
    }
}
