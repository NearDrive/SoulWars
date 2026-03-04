using Game.Client.Headless.Diagnostics;
using Game.Client.Headless.Runtime;
using Game.Protocol;
using Game.Client.Headless.Transport;

namespace Game.Client.Headless;

public sealed class HeadlessClientRunner
{
    private const int MaxFrameLength = 1024 * 1024;
    private const int RetryCastCooldownTicks = 6;
    private const int TileStepRaw = 1 << 16;

    private readonly IClientTransport _transport;
    private readonly ClientOptions _options;
    private readonly ClientWorldView _world = new();

    public HeadlessClientRunner(IClientTransport transport, ClientOptions options)
    {
        _transport = transport;
        _options = options;
    }

    public int PlayerEntityId { get; private set; }

    public bool HandshakeAccepted { get; private set; }

    public async Task<ClientRunResult> RunAsync(int maxTicks, CancellationToken cancellationToken)
    {
        await _transport.ConnectAsync(_options.Host, _options.Port, cancellationToken).ConfigureAwait(false);

        Send(new HandshakeRequest(_options.ProtocolVersion, _options.AccountId));
        Send(new EnterZoneRequestV2(_options.ZoneId));
        Send(new ClientAckV2(_options.ZoneId, 0));

        int inputTick = 1;
        bool castSent = false;
        int nextCastTick = 0;
        int lastProcessedSnapshotSeq = 0;
        List<string> logs = new();
        List<InputCommand> sentInputs = new();
        List<CastSkillCommand> sentCasts = new();
        List<HitEventV1> observedHits = new();
        ClientTraceRecorder traceRecorder = new();
        ClientFrameDecoder decoder = new(MaxFrameLength);

        while (!cancellationToken.IsCancellationRequested)
        {
            bool hadPayload = false;
            while (_transport.TryRead(out byte[] chunk))
            {
                hadPayload = true;
                decoder.Push(chunk);

                while (decoder.TryDequeueFrame(out byte[] payload))
                {
                    ClientRunResult? result = HandleServerMessage(
                        ProtocolCodec.DecodeServer(payload),
                        maxTicks,
                        logs,
                        sentInputs,
                        sentCasts,
                        observedHits,
                        traceRecorder,
                        ref inputTick,
                        ref castSent,
                        ref nextCastTick,
                        ref lastProcessedSnapshotSeq);
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }

            if (!hadPayload)
            {
                await Task.Yield();
            }
        }

        return BuildResult(logs, sentInputs, sentCasts, observedHits, HandshakeAccepted, _world.Tick, traceRecorder);
    }

    private ClientRunResult? HandleServerMessage(
        IServerMessage message,
        int maxTicks,
        List<string> logs,
        List<InputCommand> sentInputs,
        List<CastSkillCommand> sentCasts,
        List<HitEventV1> observedHits,
        ClientTraceRecorder traceRecorder,
        ref int inputTick,
        ref bool castSent,
        ref int nextCastTick,
        ref int lastProcessedSnapshotSeq)
    {
        switch (message)
        {
            case Welcome:
                HandshakeAccepted = true;
                logs.Add("welcome protocol=1");
                return null;
            case EnterZoneAck ack:
                PlayerEntityId = ack.EntityId;
                logs.Add($"enter-zone zone={ack.ZoneId} entity={ack.EntityId}");
                return null;
            case SnapshotV2 snapshot:
                Send(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq));
                if (snapshot.SnapshotSeq <= lastProcessedSnapshotSeq)
                {
                    return null;
                }

                lastProcessedSnapshotSeq = snapshot.SnapshotSeq;
                _world.ApplySnapshot(snapshot);
                traceRecorder.RecordTick(snapshot, _world.GetVisibleEntityIdsCanonical());
                logs.Add(_world.DumpCanonical());

                if (PlayerEntityId != 0 && inputTick <= snapshot.Tick + 1)
                {
                    InputCommand move = new(inputTick, 1, 0);
                    Send(move);
                    sentInputs.Add(move);
                    inputTick++;
                }

                bool shouldTryCast = !castSent || (!_options.StopOnFirstHit && observedHits.Count == 0 && snapshot.Tick >= nextCastTick);
                if (shouldTryCast && PlayerEntityId != 0)
                {
                    SnapshotEntity? self = snapshot.Entities.FirstOrDefault(entity => entity.EntityId == PlayerEntityId);
                    if (self is null)
                    {
                        return null;
                    }

                    SnapshotEntity? target = snapshot.Entities
                        .Where(entity => entity.EntityId != PlayerEntityId)
                        .OrderBy(entity => DistanceSq(self, entity))
                        .ThenBy(entity => entity.EntityId)
                        .FirstOrDefault();

                    if (target is not null || (!castSent && !_options.StopOnFirstHit))
                    {
                        int targetPosXRaw = target?.PosXRaw ?? (self.PosXRaw - TileStepRaw);
                        int targetPosYRaw = target?.PosYRaw ?? self.PosYRaw;

                        CastSkillCommand cast = new(
                            Tick: Math.Max(inputTick, snapshot.Tick + 1),
                            CasterId: PlayerEntityId,
                            SkillId: _options.AbilityId,
                            ZoneId: snapshot.ZoneId,
                            TargetKind: 3,
                            TargetEntityId: 0,
                            TargetPosXRaw: targetPosXRaw,
                            TargetPosYRaw: targetPosYRaw);
                        Send(cast);
                        sentCasts.Add(cast);
                        castSent = true;
                        nextCastTick = snapshot.Tick + RetryCastCooldownTicks;
                        logs.Add($"cast ability={_options.AbilityId} x={targetPosXRaw} y={targetPosYRaw}");
                    }
                }

                if (_world.LastHitEvents.Count > 0)
                {
                    bool firstObservedHit = observedHits.Count == 0;
                    observedHits.AddRange(_world.LastHitEvents);
                    if (firstObservedHit)
                    {
                        HitEventV1 hit = _world.LastHitEvents.First();
                        logs.Add($"hit ability={hit.AbilityId} src={hit.SourceEntityId} dst={hit.TargetEntityId} tick={hit.TickId}");
                    }
                }

                if (_options.StopOnFirstHit && observedHits.Count > 0)
                {
                    return BuildResult(logs, sentInputs, sentCasts, observedHits, HandshakeAccepted, _world.Tick, traceRecorder);
                }

                if (snapshot.Tick >= maxTicks)
                {
                    return BuildResult(logs, sentInputs, sentCasts, observedHits, HandshakeAccepted, _world.Tick, traceRecorder);
                }

                return null;
            case Disconnect disconnect:
                logs.Add($"disconnect reason={disconnect.Reason}");
                return BuildResult(logs, sentInputs, sentCasts, observedHits, HandshakeAccepted, _world.Tick, traceRecorder);
            default:
                return null;
        }
    }

    private void Send(IClientMessage message)
    {
        _transport.Send(ProtocolCodec.Encode(message));
    }

    private static long DistanceSq(SnapshotEntity a, SnapshotEntity b)
    {
        long dx = (long)a.PosXRaw - b.PosXRaw;
        long dy = (long)a.PosYRaw - b.PosYRaw;
        return dx * dx + dy * dy;
    }

    private static ClientRunResult BuildResult(
        IReadOnlyList<string> logs,
        IReadOnlyList<InputCommand> sentInputs,
        IReadOnlyList<CastSkillCommand> sentCasts,
        IReadOnlyList<HitEventV1> observedHits,
        bool handshakeAccepted,
        int totalTicks,
        ClientTraceRecorder traceRecorder)
    {
        string canonicalTrace = traceRecorder.BuildCanonicalTraceDump();
        string traceHash = traceRecorder.ComputeTraceHash();
        return new ClientRunResult(
            logs,
            sentInputs,
            sentCasts,
            observedHits,
            handshakeAccepted,
            totalTicks,
            traceRecorder.TotalHitEvents,
            traceHash,
            canonicalTrace);
    }
}
