namespace Game.Server;

public sealed class PerfCounters
{
    private readonly object _gate = new();

    private int _tickCount;

    private int _tickEntitiesVisited;
    private int _tickAoiDistanceChecks;
    private int _tickVisibilityCellsVisited;
    private int _tickVisibilityRaysCast;
    private int _tickAoiEntitiesConsidered;
    private int _tickRedactionEntitiesEmitted;
    private int _tickTransitionSpawns;
    private int _tickTransitionDespawns;
    private int _tickCollisionChecks;
    private int _tickCommandsProcessed;
    private int _tickSnapshotsEncodedEntities;
    private int _tickOutboundBytes;
    private int _tickInboundBytes;
    private int _tickOutboundMessages;
    private int _tickInboundMessages;

    private long _totalEntitiesVisited;
    private long _totalAoiDistanceChecks;
    private long _totalVisibilityCellsVisited;
    private long _totalVisibilityRaysCast;
    private long _totalAoiEntitiesConsidered;
    private long _totalRedactionEntitiesEmitted;
    private long _totalTransitionSpawns;
    private long _totalTransitionDespawns;
    private long _totalCollisionChecks;
    private long _totalCommandsProcessed;
    private long _totalSnapshotsEncodedEntities;
    private long _totalOutboundBytes;
    private long _totalInboundBytes;
    private long _totalOutboundMessages;
    private long _totalInboundMessages;

    private int _maxEntitiesVisitedPerTick;
    private int _maxAoiDistanceChecksPerTick;
    private int _maxVisibilityCellsVisitedPerTick;
    private int _maxVisibilityRaysCastPerTick;
    private int _maxAoiEntitiesConsideredPerTick;
    private int _maxRedactionEntitiesEmittedPerTick;
    private int _maxTransitionSpawnsPerTick;
    private int _maxTransitionDespawnsPerTick;
    private int _maxCollisionChecksPerTick;
    private int _maxCommandsProcessedPerTick;
    private int _maxSnapshotsEncodedEntitiesPerTick;
    private int _maxOutboundBytesPerTick;
    private int _maxInboundBytesPerTick;
    private int _maxOutboundMessagesPerTick;
    private int _maxInboundMessagesPerTick;

    public void ResetTick()
    {
        lock (_gate)
        {
            _tickEntitiesVisited = 0;
            _tickAoiDistanceChecks = 0;
            _tickVisibilityCellsVisited = 0;
            _tickVisibilityRaysCast = 0;
            _tickAoiEntitiesConsidered = 0;
            _tickRedactionEntitiesEmitted = 0;
            _tickTransitionSpawns = 0;
            _tickTransitionDespawns = 0;
            _tickCollisionChecks = 0;
            _tickCommandsProcessed = 0;
            _tickSnapshotsEncodedEntities = 0;
            _tickOutboundBytes = 0;
            _tickInboundBytes = 0;
            _tickOutboundMessages = 0;
            _tickInboundMessages = 0;
        }
    }

    public void CountEntitiesVisited(int n) => Add(ref _tickEntitiesVisited, n);

    public void CountAoiChecks(int n) => Add(ref _tickAoiDistanceChecks, n);

    public void CountVisibilityCellsVisited(int n) => Add(ref _tickVisibilityCellsVisited, n);

    public void CountVisibilityRaysCast(int n) => Add(ref _tickVisibilityRaysCast, n);

    public void CountAoiEntitiesConsidered(int n) => Add(ref _tickAoiEntitiesConsidered, n);

    public void CountRedactionEntitiesEmitted(int n) => Add(ref _tickRedactionEntitiesEmitted, n);

    public void CountTransitionSpawns(int n) => Add(ref _tickTransitionSpawns, n);

    public void CountTransitionDespawns(int n) => Add(ref _tickTransitionDespawns, n);

    public void CountCollisionChecks(int n) => Add(ref _tickCollisionChecks, n);

    public void CountCommandsProcessed(int n) => Add(ref _tickCommandsProcessed, n);

    public void CountSnapshotsEncodedEntities(int n) => Add(ref _tickSnapshotsEncodedEntities, n);

    public void AddOutboundBytes(int bytes) => Add(ref _tickOutboundBytes, bytes);

    public void AddInboundBytes(int bytes) => Add(ref _tickInboundBytes, bytes);

    public void AddOutboundMessages(int n) => Add(ref _tickOutboundMessages, n);

    public void AddInboundMessages(int n) => Add(ref _tickInboundMessages, n);

    public void CompleteTick()
    {
        lock (_gate)
        {
            _tickCount++;
            _totalEntitiesVisited += _tickEntitiesVisited;
            _totalAoiDistanceChecks += _tickAoiDistanceChecks;
            _totalVisibilityCellsVisited += _tickVisibilityCellsVisited;
            _totalVisibilityRaysCast += _tickVisibilityRaysCast;
            _totalAoiEntitiesConsidered += _tickAoiEntitiesConsidered;
            _totalRedactionEntitiesEmitted += _tickRedactionEntitiesEmitted;
            _totalTransitionSpawns += _tickTransitionSpawns;
            _totalTransitionDespawns += _tickTransitionDespawns;
            _totalCollisionChecks += _tickCollisionChecks;
            _totalCommandsProcessed += _tickCommandsProcessed;
            _totalSnapshotsEncodedEntities += _tickSnapshotsEncodedEntities;
            _totalOutboundBytes += _tickOutboundBytes;
            _totalInboundBytes += _tickInboundBytes;
            _totalOutboundMessages += _tickOutboundMessages;
            _totalInboundMessages += _tickInboundMessages;

            _maxEntitiesVisitedPerTick = Math.Max(_maxEntitiesVisitedPerTick, _tickEntitiesVisited);
            _maxAoiDistanceChecksPerTick = Math.Max(_maxAoiDistanceChecksPerTick, _tickAoiDistanceChecks);
            _maxVisibilityCellsVisitedPerTick = Math.Max(_maxVisibilityCellsVisitedPerTick, _tickVisibilityCellsVisited);
            _maxVisibilityRaysCastPerTick = Math.Max(_maxVisibilityRaysCastPerTick, _tickVisibilityRaysCast);
            _maxAoiEntitiesConsideredPerTick = Math.Max(_maxAoiEntitiesConsideredPerTick, _tickAoiEntitiesConsidered);
            _maxRedactionEntitiesEmittedPerTick = Math.Max(_maxRedactionEntitiesEmittedPerTick, _tickRedactionEntitiesEmitted);
            _maxTransitionSpawnsPerTick = Math.Max(_maxTransitionSpawnsPerTick, _tickTransitionSpawns);
            _maxTransitionDespawnsPerTick = Math.Max(_maxTransitionDespawnsPerTick, _tickTransitionDespawns);
            _maxCollisionChecksPerTick = Math.Max(_maxCollisionChecksPerTick, _tickCollisionChecks);
            _maxCommandsProcessedPerTick = Math.Max(_maxCommandsProcessedPerTick, _tickCommandsProcessed);
            _maxSnapshotsEncodedEntitiesPerTick = Math.Max(_maxSnapshotsEncodedEntitiesPerTick, _tickSnapshotsEncodedEntities);
            _maxOutboundBytesPerTick = Math.Max(_maxOutboundBytesPerTick, _tickOutboundBytes);
            _maxInboundBytesPerTick = Math.Max(_maxInboundBytesPerTick, _tickInboundBytes);
            _maxOutboundMessagesPerTick = Math.Max(_maxOutboundMessagesPerTick, _tickOutboundMessages);
            _maxInboundMessagesPerTick = Math.Max(_maxInboundMessagesPerTick, _tickInboundMessages);
        }
    }

    public PerfSnapshot SnapshotAndResetWindow()
    {
        lock (_gate)
        {
            PerfSnapshot snapshot = new(
                TickCount: _tickCount,
                TotalEntitiesVisited: _totalEntitiesVisited,
                MaxEntitiesVisitedPerTick: _maxEntitiesVisitedPerTick,
                TotalAoiDistanceChecks: _totalAoiDistanceChecks,
                MaxAoiDistanceChecksPerTick: _maxAoiDistanceChecksPerTick,
                TotalVisibilityCellsVisited: _totalVisibilityCellsVisited,
                MaxVisibilityCellsVisitedPerTick: _maxVisibilityCellsVisitedPerTick,
                TotalVisibilityRaysCast: _totalVisibilityRaysCast,
                MaxVisibilityRaysCastPerTick: _maxVisibilityRaysCastPerTick,
                TotalAoiEntitiesConsidered: _totalAoiEntitiesConsidered,
                MaxAoiEntitiesConsideredPerTick: _maxAoiEntitiesConsideredPerTick,
                TotalRedactionEntitiesEmitted: _totalRedactionEntitiesEmitted,
                MaxRedactionEntitiesEmittedPerTick: _maxRedactionEntitiesEmittedPerTick,
                TotalTransitionSpawns: _totalTransitionSpawns,
                MaxTransitionSpawnsPerTick: _maxTransitionSpawnsPerTick,
                TotalTransitionDespawns: _totalTransitionDespawns,
                MaxTransitionDespawnsPerTick: _maxTransitionDespawnsPerTick,
                TotalCollisionChecks: _totalCollisionChecks,
                MaxCollisionChecksPerTick: _maxCollisionChecksPerTick,
                TotalCommandsProcessed: _totalCommandsProcessed,
                MaxCommandsProcessedPerTick: _maxCommandsProcessedPerTick,
                TotalSnapshotsEncodedEntities: _totalSnapshotsEncodedEntities,
                MaxSnapshotsEncodedEntitiesPerTick: _maxSnapshotsEncodedEntitiesPerTick,
                TotalOutboundBytes: _totalOutboundBytes,
                MaxOutboundBytesPerTick: _maxOutboundBytesPerTick,
                TotalInboundBytes: _totalInboundBytes,
                MaxInboundBytesPerTick: _maxInboundBytesPerTick,
                TotalOutboundMessages: _totalOutboundMessages,
                MaxOutboundMessagesPerTick: _maxOutboundMessagesPerTick,
                TotalInboundMessages: _totalInboundMessages,
                MaxInboundMessagesPerTick: _maxInboundMessagesPerTick);

            _tickCount = 0;
            _totalEntitiesVisited = 0;
            _totalAoiDistanceChecks = 0;
            _totalVisibilityCellsVisited = 0;
            _totalVisibilityRaysCast = 0;
            _totalAoiEntitiesConsidered = 0;
            _totalRedactionEntitiesEmitted = 0;
            _totalTransitionSpawns = 0;
            _totalTransitionDespawns = 0;
            _totalCollisionChecks = 0;
            _totalCommandsProcessed = 0;
            _totalSnapshotsEncodedEntities = 0;
            _totalOutboundBytes = 0;
            _totalInboundBytes = 0;
            _totalOutboundMessages = 0;
            _totalInboundMessages = 0;
            _maxEntitiesVisitedPerTick = 0;
            _maxAoiDistanceChecksPerTick = 0;
            _maxVisibilityCellsVisitedPerTick = 0;
            _maxVisibilityRaysCastPerTick = 0;
            _maxAoiEntitiesConsideredPerTick = 0;
            _maxRedactionEntitiesEmittedPerTick = 0;
            _maxTransitionSpawnsPerTick = 0;
            _maxTransitionDespawnsPerTick = 0;
            _maxCollisionChecksPerTick = 0;
            _maxCommandsProcessedPerTick = 0;
            _maxSnapshotsEncodedEntitiesPerTick = 0;
            _maxOutboundBytesPerTick = 0;
            _maxInboundBytesPerTick = 0;
            _maxOutboundMessagesPerTick = 0;
            _maxInboundMessagesPerTick = 0;

            ResetTick();
            return snapshot;
        }
    }

    private void Add(ref int field, int n)
    {
        if (n <= 0)
        {
            return;
        }

        lock (_gate)
        {
            field += n;
        }
    }
}
