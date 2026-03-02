using Game.Client.Headless;
using Game.Server;

namespace Game.Server.Tests.ClientSmoke;

internal sealed class InMemoryClientTransport : IClientTransport
{
    private readonly InMemoryEndpoint _endpoint;

    public InMemoryClientTransport(InMemoryEndpoint endpoint)
    {
        _endpoint = endpoint;
    }

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken) => Task.CompletedTask;

    public void Send(byte[] payload) => _endpoint.EnqueueToServer(payload);

    public bool TryRead(out byte[] payload) => _endpoint.TryDequeueFromServer(out payload!);

    public ValueTask DisposeAsync()
    {
        _endpoint.Close();
        return ValueTask.CompletedTask;
    }
}
