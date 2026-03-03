using Game.Client.Headless;

namespace Game.Server.Tests.ClientSmoke;

internal sealed class DelayedClientTransport : IClientTransport
{
    private readonly IClientTransport _inner;
    private readonly int _delayTicks;
    private readonly Queue<ScheduledFrame> _scheduled = new();
    private int _currentTick;

    public DelayedClientTransport(IClientTransport inner, int delayTicks)
    {
        if (delayTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayTicks), "Delay ticks must be greater than or equal to zero.");
        }

        _inner = inner;
        _delayTicks = delayTicks;
    }

    public int CurrentTick => _currentTick;

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        => _inner.ConnectAsync(host, port, cancellationToken);

    public void Send(byte[] payload) => _inner.Send(payload);

    public bool TryRead(out byte[] payload)
    {
        PumpInnerFrames();

        if (_scheduled.Count == 0 || _scheduled.Peek().DeliverAtTick > _currentTick)
        {
            payload = Array.Empty<byte>();
            return false;
        }

        payload = _scheduled.Dequeue().Payload;
        return true;
    }

    public void AdvanceTick()
    {
        _currentTick++;
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    private void PumpInnerFrames()
    {
        while (_inner.TryRead(out byte[] frame))
        {
            _scheduled.Enqueue(new ScheduledFrame(frame, _currentTick + _delayTicks));
        }
    }

    private sealed record ScheduledFrame(byte[] Payload, int DeliverAtTick);
}
