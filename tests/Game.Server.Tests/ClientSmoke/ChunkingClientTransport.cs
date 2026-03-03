using Game.Client.Headless;

namespace Game.Server.Tests.ClientSmoke;

internal sealed class ChunkingClientTransport : IClientTransport
{
    private readonly IClientTransport _inner;
    private readonly int[] _chunkPattern;
    private readonly Queue<byte> _buffer = new();
    private int _patternIndex;

    public ChunkingClientTransport(IClientTransport inner, IReadOnlyList<int> chunkPattern)
    {
        _inner = inner;
        _chunkPattern = chunkPattern.ToArray();
        if (_chunkPattern.Length == 0 || _chunkPattern.Any(size => size <= 0))
        {
            throw new ArgumentException("Chunk pattern must contain only positive sizes.", nameof(chunkPattern));
        }
    }

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        => _inner.ConnectAsync(host, port, cancellationToken);

    public void Send(byte[] payload) => _inner.Send(payload);

    public bool TryRead(out byte[] payload)
    {
        if (_buffer.Count == 0)
        {
            if (!_inner.TryRead(out byte[] source))
            {
                payload = Array.Empty<byte>();
                return false;
            }

            for (int i = 0; i < source.Length; i++)
            {
                _buffer.Enqueue(source[i]);
            }
        }

        int chunkSize = _chunkPattern[_patternIndex % _chunkPattern.Length];
        _patternIndex++;

        int actual = Math.Min(chunkSize, _buffer.Count);
        byte[] chunk = new byte[actual];
        for (int i = 0; i < actual; i++)
        {
            chunk[i] = _buffer.Dequeue();
        }

        payload = chunk;
        return true;
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
