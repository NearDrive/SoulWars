using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Game.Server;

public sealed class TcpEndpoint : IServerEndpoint, IAsyncDisposable
{
    private const int MaxFrameLength = 1024 * 1024;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ConcurrentQueue<byte[]> _inbound = new();
    private readonly ConcurrentQueue<byte[]> _outbound = new();
    private readonly SemaphoreSlim _outboundSignal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readerTask;
    private readonly Task _writerTask;

    public TcpEndpoint(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _readerTask = Task.Run(ReadLoopAsync);
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public bool TryDequeueToServer(out byte[] msg) => _inbound.TryDequeue(out msg!);

    public void EnqueueToClient(byte[] msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        if (_cts.IsCancellationRequested)
        {
            return;
        }

        _outbound.Enqueue(msg);
        _outboundSignal.Release();
    }

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

        await Task.WhenAll(_readerTask, _writerTask).ConfigureAwait(false);

        _outboundSignal.Dispose();
        _cts.Dispose();
        _stream.Dispose();
        _client.Dispose();
    }

    private async Task ReadLoopAsync()
    {
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
