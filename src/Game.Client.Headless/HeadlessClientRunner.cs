using Game.Protocol;

namespace Game.Client.Headless;

public sealed class HeadlessClientRunner
{
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

    public async Task<HeadlessRunResult> RunAsync(int maxTicks, CancellationToken cancellationToken)
    {
        await _transport.ConnectAsync(_options.Host, _options.Port, cancellationToken).ConfigureAwait(false);

        Send(new HandshakeRequest(_options.ProtocolVersion, _options.AccountId));
        Send(new EnterZoneRequestV2(_options.ZoneId));
        Send(new ClientAckV2(_options.ZoneId, 0));

        int inputTick = 1;
        bool castSent = false;
        List<string> logs = new();
        List<InputCommand> sentInputs = new();
        List<CastSkillCommand> sentCasts = new();
        List<HitEventV1> observedHits = new();

        while (!cancellationToken.IsCancellationRequested)
        {
            bool hadPayload = false;
            while (_transport.TryRead(out byte[] payload))
            {
                hadPayload = true;
                IServerMessage message = ProtocolCodec.DecodeServer(payload);
                switch (message)
                {
                    case Welcome:
                        HandshakeAccepted = true;
                        logs.Add("welcome protocol=1");
                        break;
                    case EnterZoneAck ack:
                        PlayerEntityId = ack.EntityId;
                        logs.Add($"enter-zone zone={ack.ZoneId} entity={ack.EntityId}");
                        break;
                    case SnapshotV2 snapshot:
                        _world.ApplySnapshot(snapshot);
                        Send(new ClientAckV2(snapshot.ZoneId, snapshot.SnapshotSeq));
                        logs.Add(_world.DumpCanonical());

                        if (PlayerEntityId != 0 && inputTick <= snapshot.Tick + 1)
                        {
                            InputCommand move = new(inputTick, 1, 0);
                            Send(move);
                            sentInputs.Add(move);
                            inputTick++;
                        }

                        if (!castSent && PlayerEntityId != 0)
                        {
                            SnapshotEntity? self = snapshot.Entities.FirstOrDefault(entity => entity.EntityId == PlayerEntityId);
                            SnapshotEntity? target = snapshot.Entities
                                .Where(entity => entity.EntityId != PlayerEntityId)
                                .OrderBy(entity => self is null ? int.MaxValue : DistanceSq(self, entity))
                                .ThenBy(entity => entity.EntityId)
                                .FirstOrDefault();

                            if (target is not null)
                            {
                                CastSkillCommand cast = new(
                                    Tick: Math.Max(inputTick, snapshot.Tick + 1),
                                    CasterId: PlayerEntityId,
                                    SkillId: _options.AbilityId,
                                    ZoneId: snapshot.ZoneId,
                                    TargetKind: 3,
                                    TargetEntityId: 0,
                                    TargetPosXRaw: target.PosXRaw,
                                    TargetPosYRaw: target.PosYRaw);
                                Send(cast);
                                sentCasts.Add(cast);
                                castSent = true;
                                logs.Add($"cast ability={_options.AbilityId} x={target.PosXRaw} y={target.PosYRaw}");
                            }
                        }

                        if (_world.LastHitEvents.Count > 0)
                        {
                            HitEventV1 hit = _world.LastHitEvents.First();
                            observedHits.AddRange(_world.LastHitEvents);
                            logs.Add($"hit ability={hit.AbilityId} src={hit.SourceEntityId} dst={hit.TargetEntityId} tick={hit.TickId}");
                            return new HeadlessRunResult(logs, sentInputs, sentCasts, observedHits, HandshakeAccepted);
                        }

                        if (snapshot.Tick >= maxTicks)
                        {
                            return new HeadlessRunResult(logs, sentInputs, sentCasts, observedHits, HandshakeAccepted);
                        }

                        break;
                    case Disconnect disconnect:
                        logs.Add($"disconnect reason={disconnect.Reason}");
                        return new HeadlessRunResult(logs, sentInputs, sentCasts, observedHits, HandshakeAccepted);
                }
            }

            if (!hadPayload)
            {
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
        }

        return new HeadlessRunResult(logs, sentInputs, sentCasts, observedHits, HandshakeAccepted);
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
}

public sealed record HeadlessRunResult(
    IReadOnlyList<string> Logs,
    IReadOnlyList<InputCommand> SentInputs,
    IReadOnlyList<CastSkillCommand> SentCasts,
    IReadOnlyList<HitEventV1> ObservedHits,
    bool HandshakeAccepted)
{
    public bool HitObserved => ObservedHits.Count > 0;
}
