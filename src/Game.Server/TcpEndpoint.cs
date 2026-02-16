using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Game.Server;

public sealed class TcpEndpoint : IServerEndpoint, IAsyncDisposable
{
    public const int MaxFrameBytes = 1024 * 1024;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ConcurrentQueue<byte[]> _inbound = new();
    private readonly ConcurrentQueue<byte[]> _outbound = new();
    private readonly SemaphoreSlim _outboundSignal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readerTask;
    private readonly Task _writerTask;
    private readonly ILogger<TcpEndpoint> _logger;
    private readonly FrameDecoder _frameDecoder = new(MaxFrameBytes);

    public string EndpointKey { get; }

    public TcpEndpoint(TcpClient client, ILogger<TcpEndpoint>? logger = null)
    {
        _client = client;
        _stream = client.GetStream();
        EndpointKey = client.Client.RemoteEndPoint?.ToString() ?? $"tcp-{Guid.NewGuid():N}";
        _logger = logger ?? NullLogger<TcpEndpoint>.Instance;
        _readerTask = Task.Run(ReadLoopAsync);
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public bool IsClosed => _cts.IsCancellationRequested;

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
        _frameDecoder.Close();
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
        byte[] readBuffer = new byte[4096];

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int read = await _stream.ReadAsync(readBuffer, _cts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    _logger.LogInformation(ServerLogEvents.SessionDisconnected, "SessionDisconnected reason={Reason}", "tcp_eof");
                    break;
                }

                _frameDecoder.Push(readBuffer.AsSpan(0, read));
                if (_frameDecoder.IsClosed)
                {
                    _logger.LogWarning(ServerLogEvents.OversizedMessage, "OversizedMessage reason={Reason}", "invalid_frame_length");
                    break;
                }

                while (_frameDecoder.TryDequeueFrame(out byte[] frame))
                {
                    _inbound.Enqueue(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            _logger.LogInformation(ServerLogEvents.SessionDisconnected, "SessionDisconnected reason={Reason}", "tcp_io_error");
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ServerLogEvents.UnhandledException, ex, "UnhandledException");
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
                    if (payload.Length <= 0 || payload.Length > MaxFrameBytes)
                    {
                        _logger.LogWarning(ServerLogEvents.OversizedMessage, "OversizedMessage len={Length}", payload.Length);
                        Close();
                        return;
                    }

                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(framePrefix, payload.Length);
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
            _logger.LogInformation(ServerLogEvents.SessionDisconnected, "SessionDisconnected reason={Reason}", "tcp_write_io_error");
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ServerLogEvents.UnhandledException, ex, "UnhandledException");
        }
        finally
        {
            Close();
        }
    }
}
