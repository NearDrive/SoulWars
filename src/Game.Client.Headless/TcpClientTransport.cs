using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Game.Client.Headless;

public sealed class TcpClientTransport : IClientTransport
{
    private const int MaxFrameLength = 1024 * 1024;

    private readonly TcpClient _client = new();
    private readonly ConcurrentQueue<byte[]> _inbound = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _writeGate = new();

    private NetworkStream? _stream;
    private Task? _readerTask;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        await _client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        _stream = _client.GetStream();
        _readerTask = Task.Run(ReadLoopAsync);
    }

    public void Send(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (_cts.IsCancellationRequested || _stream is null)
        {
            return;
        }

        byte[] framePrefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(framePrefix, payload.Length);

        lock (_writeGate)
        {
            _stream.Write(framePrefix);
            _stream.Write(payload);
        }
    }

    public bool TryRead(out byte[] payload) => _inbound.TryDequeue(out payload!);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            _client.Close();
        }
        catch
        {
        }

        if (_readerTask is not null)
        {
            await _readerTask.ConfigureAwait(false);
        }

        _stream?.Dispose();
        _client.Dispose();
        _cts.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        if (_stream is null)
        {
            return;
        }

        byte[] lengthBuffer = new byte[4];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await ReadExactlyAsync(_stream, lengthBuffer, _cts.Token).ConfigureAwait(false);
                int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
                if (length <= 0 || length > MaxFrameLength)
                {
                    break;
                }

                byte[] payload = new byte[length];
                await ReadExactlyAsync(_stream, payload, _cts.Token).ConfigureAwait(false);
                byte[] frame = new byte[4 + payload.Length];
                BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), payload.Length);
                payload.CopyTo(frame, 4);
                _inbound.Enqueue(frame);
            }
        }
        catch
        {
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("Connection closed while reading frame.");
            }

            totalRead += read;
        }
    }
}
