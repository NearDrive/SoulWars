using System.Diagnostics;

namespace Game.Server;

public sealed class ServerMetrics
{
    private readonly object _gate = new();
    private readonly double[] _tickWindowMs;
    private int _tickWindowCount;
    private int _tickWindowWriteIndex;

    private int _playersConnected;
    private long _messagesIn;
    private long _messagesOut;

    private int _lastSnapshotTick;
    private long _lastSnapshotMessagesIn;
    private long _lastSnapshotMessagesOut;

    public ServerMetrics(int tickWindowSize = 512)
    {
        if (tickWindowSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickWindowSize));
        }

        _tickWindowMs = new double[tickWindowSize];
    }

    public long MessagesIn => Interlocked.Read(ref _messagesIn);

    public long MessagesOut => Interlocked.Read(ref _messagesOut);

    public int PlayersConnected => Volatile.Read(ref _playersConnected);

    public void IncrementMessagesIn() => Interlocked.Increment(ref _messagesIn);

    public void IncrementMessagesOut() => Interlocked.Increment(ref _messagesOut);

    public void SetPlayersConnected(int count) => Volatile.Write(ref _playersConnected, Math.Max(0, count));

    public void RecordTick(long elapsedStopwatchTicks)
    {
        double elapsedMs = elapsedStopwatchTicks * 1000d / Stopwatch.Frequency;
        lock (_gate)
        {
            _tickWindowMs[_tickWindowWriteIndex] = elapsedMs;
            _tickWindowWriteIndex = (_tickWindowWriteIndex + 1) % _tickWindowMs.Length;
            if (_tickWindowCount < _tickWindowMs.Length)
            {
                _tickWindowCount++;
            }
        }
    }

    public ServerMetricsSnapshot Snapshot(int tick, int tickHz)
    {
        if (tickHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickHz));
        }

        double tickAvg;
        double tickP95;
        int windowCount;

        lock (_gate)
        {
            windowCount = _tickWindowCount;
            if (windowCount == 0)
            {
                tickAvg = 0;
                tickP95 = 0;
            }
            else
            {
                double[] samples = new double[windowCount];
                Array.Copy(_tickWindowMs, samples, windowCount);
                tickAvg = samples.Average();
                Array.Sort(samples);
                int p95Index = (int)Math.Ceiling(windowCount * 0.95) - 1;
                p95Index = Math.Clamp(p95Index, 0, windowCount - 1);
                tickP95 = samples[p95Index];
            }
        }

        long inTotal = MessagesIn;
        long outTotal = MessagesOut;

        int deltaTicks = tick - _lastSnapshotTick;
        long deltaIn = inTotal - _lastSnapshotMessagesIn;
        long deltaOut = outTotal - _lastSnapshotMessagesOut;

        double inPerSec = 0;
        double outPerSec = 0;
        if (deltaTicks > 0)
        {
            double deltaSeconds = deltaTicks / (double)tickHz;
            inPerSec = deltaIn / deltaSeconds;
            outPerSec = deltaOut / deltaSeconds;
        }

        _lastSnapshotTick = tick;
        _lastSnapshotMessagesIn = inTotal;
        _lastSnapshotMessagesOut = outTotal;

        return new ServerMetricsSnapshot(
            Tick: tick,
            PlayersConnected: PlayersConnected,
            MessagesInTotal: inTotal,
            MessagesOutTotal: outTotal,
            TickAvgMsWindow: tickAvg,
            TickP95MsWindow: tickP95,
            MessagesInPerSecApprox: inPerSec,
            MessagesOutPerSecApprox: outPerSec);
    }
}

public sealed record ServerMetricsSnapshot(
    int Tick,
    int PlayersConnected,
    long MessagesInTotal,
    long MessagesOutTotal,
    double TickAvgMsWindow,
    double TickP95MsWindow,
    double MessagesInPerSecApprox,
    double MessagesOutPerSecApprox);
