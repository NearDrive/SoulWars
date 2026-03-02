namespace Game.Client.Headless;

public interface IClientTransport : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken);

    void Send(byte[] payload);

    bool TryRead(out byte[] payload);
}
