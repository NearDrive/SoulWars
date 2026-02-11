using System.Collections.Concurrent;

namespace Game.Server;

public sealed class InMemoryEndpoint : IServerEndpoint, IClientEndpoint
{
    private readonly ConcurrentQueue<byte[]> _toServer = new();
    private readonly ConcurrentQueue<byte[]> _toClient = new();

    public bool TryDequeueToServer(out byte[] msg) => _toServer.TryDequeue(out msg!);

    public void EnqueueToServer(byte[] msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        _toServer.Enqueue(msg);
    }

    public bool TryDequeueToClient(out byte[] msg) => _toClient.TryDequeue(out msg!);

    public bool TryDequeueFromServer(out byte[] msg) => TryDequeueToClient(out msg);

    public void EnqueueToClient(byte[] msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        _toClient.Enqueue(msg);
    }

    public void Close()
    {
    }
}
