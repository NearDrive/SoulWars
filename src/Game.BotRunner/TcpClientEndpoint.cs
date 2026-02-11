using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Game.BotRunner;

public sealed class TcpClientEndpoint : IAsyncDisposable
{
    private const int MaxFrameLength = 1024 * 1024;

    private readonly TcpClient _client = new();
    private readonly ConcurrentQueue<byte[]> _inbound = new();
    private readonly ConcurrentQueue<byte[]> _outbound = new();
    private readonly SemaphoreSlim _outboundSignal = new(0);
    private readonly CancellationTokenSource _cts = new();

    private NetworkStream? _stream;
    private Task? _readerTask;
    private Task? _writerTask;

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        await _client.ConnectAsync(host, port, ct).ConfigureAwait(false);
        _stream = _client.GetStream();

        _readerTask = Task.Run(ReadLoopAsync);
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public void EnqueueToServer(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (_cts.IsCancellationRequested)
        {
            return;
        }

        _outbound.Enqueue(payload);
        _outboundSignal.Release();
    }

    public bool TryDequeueFromServer(out byte[] payload) => _inbound.TryDequeue(out payload!);

    public void Close()
    {
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _client.Close();
        }
        catch
        {
        }

        _outboundSignal.Release();
    }

    public async ValueTask DisposeAsync()
    {
        Close();
        if (_readerTask is not null && _writerTask is not null)
        {
            await Task.WhenAll(_readerTask, _writerTask).ConfigureAwait(false);
        }

        _stream?.Dispose();
        _client.Dispose();
        _outboundSignal.Dispose();
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
                    throw new InvalidOperationException($"Invalid frame length {length}.");
                }

                byte[] payload = new byte[length];
                await ReadExactlyAsync(_stream, payload, _cts.Token).ConfigureAwait(false);
                _inbound.Enqueue(payload);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            Close();
        }
    }

    private async Task WriteLoopAsync()
    {
        if (_stream is null)
        {
            return;
        }

        byte[] framePrefix = new byte[4];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await _outboundSignal.WaitAsync(_cts.Token).ConfigureAwait(false);
                while (_outbound.TryDequeue(out byte[]? payload))
                {
                    BinaryPrimitives.WriteInt32LittleEndian(framePrefix, payload.Length);
                    await _stream.WriteAsync(framePrefix, _cts.Token).ConfigureAwait(false);
                    await _stream.WriteAsync(payload, _cts.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            Close();
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("Connection closed while reading frame.");
            }

            totalRead += read;
        }
    }
}
