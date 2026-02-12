using System.Net;

namespace Game.Server;

public sealed class ServerRuntime : IAsyncDisposable
{
    private readonly TcpServerTransport _transport = new();

    public ServerHost Host { get; private set; } = null!;

    public int BoundPort => _transport.BoundPort;

    public async Task StartAsync(ServerConfig cfg, IPAddress ip, int port, CancellationToken ct)
    {
        Host = new ServerHost(cfg);
        await _transport.StartAsync(ip, port, ct).ConfigureAwait(false);
    }

    public void StepOnce()
    {
        PumpTransportOnce();
        Host.ProcessInboundOnce();
        Host.AdvanceSimulationOnce();
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
