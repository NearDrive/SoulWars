using Game.Client.Headless;
using Game.Server;
using System.Buffers.Binary;

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

    public bool TryRead(out byte[] payload)
    {
        if (!_endpoint.TryDequeueFromServer(out byte[] message))
        {
            payload = Array.Empty<byte>();
            return false;
        }

        byte[] frame = new byte[4 + message.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), message.Length);
        message.CopyTo(frame, 4);
        payload = frame;
        return true;
    }

    public ValueTask DisposeAsync()
    {
        _endpoint.Close();
        return ValueTask.CompletedTask;
    }
}
