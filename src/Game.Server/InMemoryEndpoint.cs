using System.Collections.Concurrent;

namespace Game.Server;

public sealed class InMemoryEndpoint : IServerEndpoint, IClientEndpoint
{
    private static int _nextId = 0;

    private readonly ConcurrentQueue<byte[]> _toServer = new();
    private readonly ConcurrentQueue<byte[]> _toClient = new();

    public InMemoryEndpoint()
    {
        EndpointKey = $"mem-{Interlocked.Increment(ref _nextId)}";
    }

    public string EndpointKey { get; }

    public bool IsClosed { get; private set; }

    public int PendingToServerCount => _toServer.Count;

    public int PendingToClientCount => _toClient.Count;

    public bool TryDequeueToServer(out byte[] msg) => _toServer.TryDequeue(out msg!);

    public void EnqueueToServer(byte[] msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        if (!IsClosed)
        {
            _toServer.Enqueue(msg);
        }
    }

    public bool TryDequeueToClient(out byte[] msg) => _toClient.TryDequeue(out msg!);

    public bool TryDequeueFromServer(out byte[] msg) => TryDequeueToClient(out msg);

    public void EnqueueToClient(byte[] msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        if (!IsClosed)
        {
            _toClient.Enqueue(msg);
        }
    }

    public void Close()
    {
        IsClosed = true;
    }
}
