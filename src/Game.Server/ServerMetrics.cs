namespace Game.Server;

public sealed class ServerMetrics
{
    private readonly object _gate = new();
    private readonly double[] _tickWindowMs;
    private readonly bool _enabled;

    private int _tickWindowCount;
    private int _tickWindowWriteIndex;
    private int _playersConnected;

    private int _tickCount;
    private long _messagesIn;
    private long _messagesOut;
    private long _protocolDecodeErrors;
    private long _transportErrors;

    public ServerMetrics(bool enabled = true, int tickWindowSize = 1024)
    {
        if (tickWindowSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickWindowSize));
        }

        _enabled = enabled;
        _tickWindowMs = new double[tickWindowSize];
    }

    public long MessagesIn => Interlocked.Read(ref _messagesIn);

    public long MessagesOut => Interlocked.Read(ref _messagesOut);

    public int PlayersConnected => Volatile.Read(ref _playersConnected);

    public long ProtocolDecodeErrors => Interlocked.Read(ref _protocolDecodeErrors);

    public long TransportErrors => Interlocked.Read(ref _transportErrors);

    public void IncrementMessagesIn() => Interlocked.Increment(ref _messagesIn);

    public void IncrementMessagesOut() => Interlocked.Increment(ref _messagesOut);

    public void IncrementProtocolDecodeErrors() => Interlocked.Increment(ref _protocolDecodeErrors);

    public void IncrementTransportErrors() => Interlocked.Increment(ref _transportErrors);

    public void SetPlayersConnected(int count) => Volatile.Write(ref _playersConnected, Math.Max(0, count));

    public void OnTickCompleted(int tick, double simStepMs, int messagesIn, int messagesOut, int sessionCount)
    {
        if (!_enabled)
        {
            return;
        }

        _ = tick;
        _ = messagesIn;
        _ = messagesOut;

        lock (_gate)
        {
            _tickCount++;
            _playersConnected = Math.Max(0, sessionCount);
            _tickWindowMs[_tickWindowWriteIndex] = simStepMs;
            _tickWindowWriteIndex = (_tickWindowWriteIndex + 1) % _tickWindowMs.Length;
            if (_tickWindowCount < _tickWindowMs.Length)
            {
                _tickWindowCount++;
            }
        }
    }

    public MetricsSnapshot Snapshot() => Snapshot(tickHz: 20);

    public MetricsSnapshot SnapshotAndReset() => SnapshotAndReset(tickHz: 20);

    public MetricsSnapshot SnapshotAndReset(int tickHz)
    {
        MetricsSnapshot snapshot = Snapshot(tickHz);
        lock (_gate)
        {
            _tickCount = 0;
            _tickWindowCount = 0;
            _tickWindowWriteIndex = 0;
        }

        return snapshot;
    }

    public MetricsSnapshot Snapshot(int tickHz)
    {
        if (tickHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickHz));
        }

        lock (_gate)
        {
            if (!_enabled)
            {
                return new MetricsSnapshot(0, 0, 0, 0, 0);
            }

            int sampleCount = _tickWindowCount;
            double[] samples = new double[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int idx = (_tickWindowWriteIndex - sampleCount + i + _tickWindowMs.Length) % _tickWindowMs.Length;
                samples[i] = _tickWindowMs[idx];
            }

            if (sampleCount > 0)
            {
                Array.Sort(samples);
            }

            double p50 = sampleCount > 0 ? Percentile(samples, 0.5) : 0;
            double p95 = sampleCount > 0 ? Percentile(samples, 0.95) : 0;
            double elapsedSeconds = _tickCount > 0 ? _tickCount / (double)tickHz : 0;

            double inPerSec = elapsedSeconds > 0 ? MessagesIn / elapsedSeconds : 0;
            double outPerSec = elapsedSeconds > 0 ? MessagesOut / elapsedSeconds : 0;

            return new MetricsSnapshot(_tickCount, p50, p95, inPerSec, outPerSec);
        }
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        int index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
        index = Math.Clamp(index, 0, sorted.Length - 1);
        return sorted[index];
    }
}
