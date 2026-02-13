using System.Collections.Immutable;
using Game.Core;
using Game.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Game.Server;

public sealed class ServerHost
{
    private readonly SimulationConfig _simulationConfig;
    private readonly ServerConfig _serverConfig;
    private readonly Dictionary<int, SessionState> _sessions = new();
    private readonly ILogger<ServerHost> _logger;
    private readonly PlayerRegistry _playerRegistry = new();

    private int _nextSessionId = 1;
    private int _nextEntityId = 1;
    private readonly List<WorldCommand> _pendingWorldCommands = new();
    private readonly List<PendingAttackIntent> _pendingAttackIntents = new();
    private readonly List<Snapshot> _recentSnapshots = new();
    private WorldState _world;
    private int _lastTick;

    public ServerHost(ServerConfig config, ILoggerFactory? loggerFactory = null, ServerMetrics? metrics = null)
    {
        if (config.SnapshotEveryTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "SnapshotEveryTicks must be > 0.");
        }

        _serverConfig = config;
        _simulationConfig = config.ToSimulationConfig();
        _world = Simulation.CreateInitialState(_simulationConfig);
        ILoggerFactory factory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = factory.CreateLogger<ServerHost>();
        Metrics = metrics ?? new ServerMetrics();
        _lastTick = _world.Tick;
    }

    public ServerMetrics Metrics { get; }

    public SessionId Connect(IServerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        SessionId sessionId = new(_nextSessionId++);
        SessionState session = new(sessionId, endpoint);
        _sessions[sessionId.Value] = session;
        Metrics.SetPlayersConnected(_sessions.Count);

        _logger.LogInformation(ServerLogEvents.SessionConnected, "SessionConnected sessionId={SessionId}", sessionId.Value);
        return sessionId;
    }

    public void StepOnce()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        ProcessInboundOnce();
        AdvanceSimulationOnce();
        Metrics.RecordTick(System.Diagnostics.Stopwatch.GetTimestamp() - start);
    }

    public void ProcessInboundOnce()
    {
        int targetTick = _world.Tick + 1;

        foreach (SessionState session in OrderedSessions().ToArray())
        {
            if (session.Endpoint.IsClosed)
            {
                Metrics.IncrementTransportErrors();
                DisconnectSession(session, "endpoint_closed");
                continue;
            }

            DrainSessionMessages(session, targetTick, _pendingWorldCommands);

            if (session.Endpoint.IsClosed)
            {
                Metrics.IncrementTransportErrors();
                DisconnectSession(session, "endpoint_closed");
            }
        }
    }

    public void AdvanceSimulationOnce()
    {
        int targetTick = _world.Tick + 1;
        List<WorldCommand> worldCommands = _pendingWorldCommands.ToList();
        _pendingWorldCommands.Clear();

        foreach (PendingInput pending in OrderedPendingInputs(targetTick))
        {
            SessionState session = _sessions[pending.SessionId.Value];
            if (session.ActiveZoneId is null || session.EntityId is null)
            {
                continue;
            }

            worldCommands.Add(new WorldCommand(
                Kind: WorldCommandKind.MoveIntent,
                EntityId: new EntityId(session.EntityId.Value),
                ZoneId: new ZoneId(session.ActiveZoneId.Value),
                MoveX: pending.MoveX,
                MoveY: pending.MoveY));
        }

        foreach (PendingAttackIntent pending in OrderedPendingAttackIntents(targetTick))
        {
            if (!_sessions.TryGetValue(pending.SessionId.Value, out SessionState? session))
            {
                continue;
            }

            if (session.ActiveZoneId is null || session.EntityId is null)
            {
                continue;
            }

            if (session.ActiveZoneId.Value != pending.ZoneId)
            {
                continue;
            }

            worldCommands.Add(new WorldCommand(
                Kind: WorldCommandKind.AttackIntent,
                EntityId: new EntityId(session.EntityId.Value),
                ZoneId: new ZoneId(pending.ZoneId),
                TargetEntityId: new EntityId(pending.TargetEntityId)));
        }

        _world = Simulation.Step(_simulationConfig, _world, new Inputs(worldCommands.ToImmutableArray()));
        if (_serverConfig.Invariants.EnableCoreInvariants)
        {
            CoreInvariants.Validate(_world, _world.Tick);
        }
        SynchronizeSessionZonesWithWorld();

        if (_world.Tick % _serverConfig.SnapshotEveryTicks == 0)
        {
            EmitSnapshots();
        }

        if (_serverConfig.Invariants.EnableServerInvariants)
        {
            ServerInvariants.Validate(new ServerHostDebugView(
                LastTick: _lastTick,
                CurrentTick: _world.Tick,
                Sessions: OrderedSessions().Select(s => new ServerSessionDebugView(s.SessionId.Value, s.EntityId, s.LastSnapshotTick, s.ActiveZoneId)).ToArray(),
                Snapshots: _recentSnapshots.ToArray()));
        }
        _lastTick = _world.Tick;
        _recentSnapshots.Clear();
    }

    public void AdvanceTicks(int n)
    {
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n));
        }

        for (int i = 0; i < n; i++)
        {
            StepOnce();
        }
    }

    public ServerMetricsSnapshot SnapshotMetrics() => Metrics.Snapshot(_world.Tick, _serverConfig.TickHz);

    private void DrainSessionMessages(SessionState session, int targetTick, List<WorldCommand> worldCommands)
    {
        while (session.Endpoint.TryDequeueToServer(out byte[] payload))
        {
            if (!ProtocolCodec.TryDecodeClient(payload, out IClientMessage? message, out ProtocolErrorCode error))
            {
                Metrics.IncrementProtocolDecodeErrors();
                _logger.LogWarning(ServerLogEvents.ProtocolDecodeFailed, "ProtocolDecodeFailed sessionId={SessionId} error={Error}", session.SessionId.Value, error);
                session.Endpoint.EnqueueToClient(ProtocolCodec.Encode(new Error("protocol_error", error.ToString())));
                Metrics.IncrementMessagesOut();
                continue;
            }

            Metrics.IncrementMessagesIn();

            switch (message)
            {
                case Hello hello:
                    HandleHello(session, hello.ClientVersion, $"legacy-session-{session.SessionId.Value}");
                    break;
                case HelloV2 helloV2:
                    HandleHello(session, helloV2.ClientVersion, helloV2.AccountId);
                    break;
                case EnterZoneRequest enterZoneRequest:
                    HandleEnterZone(session, enterZoneRequest, worldCommands);
                    break;
                case InputCommand inputCommand:
                    session.PendingInputs.Add(new PendingInput(targetTick, session.SessionId, inputCommand.MoveX, inputCommand.MoveY));
                    break;
                case AttackIntent attackIntent:
                    _pendingAttackIntents.Add(new PendingAttackIntent(targetTick, session.SessionId, attackIntent.ZoneId, attackIntent.TargetId));
                    break;
                case LeaveZoneRequest leaveZoneRequest:
                    HandleLeaveZone(session, leaveZoneRequest, worldCommands);
                    break;
                case TeleportRequest teleportRequest:
                    HandleTeleport(session, teleportRequest, worldCommands);
                    break;
            }
        }
    }

    private void HandleHello(SessionState session, string clientVersion, string accountId)
    {
        string normalizedAccountId = string.IsNullOrWhiteSpace(accountId)
            ? $"anon-session-{session.SessionId.Value}"
            : accountId.Trim();

        PlayerId playerId = _playerRegistry.GetOrCreate(normalizedAccountId);
        if (_playerRegistry.TryGetActiveSession(playerId, out SessionId existingSessionId) &&
            existingSessionId.Value != session.SessionId.Value &&
            _sessions.TryGetValue(existingSessionId.Value, out SessionState? existingSession))
        {
            DisconnectSession(existingSession, "replaced_by_new_login");
        }

        _playerRegistry.AttachSession(playerId, session.SessionId);
        session.AccountId = normalizedAccountId;
        session.PlayerId = playerId;

        if (_playerRegistry.TryGetState(playerId, out PlayerState? state))
        {
            session.EntityId = state.EntityId;
            session.ActiveZoneId = state.ZoneId;
        }

        session.Endpoint.EnqueueToClient(ProtocolCodec.Encode(new Welcome(session.SessionId, playerId)));
        Metrics.IncrementMessagesOut();
    }

    private void HandleEnterZone(SessionState session, EnterZoneRequest request, List<WorldCommand> worldCommands)
    {
        if (session.PlayerId is null)
        {
            return;
        }

        if (!_playerRegistry.TryGetState(session.PlayerId.Value, out PlayerState? playerState))
        {
            return;
        }

        int entityId;
        if (playerState.EntityId is int existingEntityId &&
            _world.TryGetEntityZone(new EntityId(existingEntityId), out ZoneId existingZoneId) &&
            existingZoneId.Value == request.ZoneId)
        {
            entityId = existingEntityId;
        }
        else
        {
            entityId = playerState.EntityId ?? _nextEntityId++;
            worldCommands.Add(new WorldCommand(
                Kind: WorldCommandKind.EnterZone,
                EntityId: new EntityId(entityId),
                ZoneId: new ZoneId(request.ZoneId)));
        }

        session.EntityId = entityId;
        session.ActiveZoneId = request.ZoneId;
        _playerRegistry.UpdateWorldState(session.PlayerId.Value, entityId, request.ZoneId, isAlive: true);

        session.Endpoint.EnqueueToClient(ProtocolCodec.Encode(new EnterZoneAck(request.ZoneId, entityId)));
        Metrics.IncrementMessagesOut();

        _logger.LogInformation(
            ServerLogEvents.SessionEnteredZone,
            "SessionEnteredZone sessionId={SessionId} playerId={PlayerId} zoneId={ZoneId} entityId={EntityId}",
            session.SessionId.Value,
            session.PlayerId.Value.Value,
            request.ZoneId,
            entityId);
    }


    private void HandleTeleport(SessionState session, TeleportRequest request, List<WorldCommand> worldCommands)
    {
        if (session.EntityId is null || session.ActiveZoneId is null)
        {
            return;
        }

        int fromZoneId = session.ActiveZoneId.Value;
        if (request.ToZoneId <= 0 || request.ToZoneId > _simulationConfig.ZoneCount || request.ToZoneId == fromZoneId)
        {
            return;
        }

        worldCommands.Add(new WorldCommand(
            Kind: WorldCommandKind.TeleportIntent,
            EntityId: new EntityId(session.EntityId.Value),
            ZoneId: new ZoneId(fromZoneId),
            ToZoneId: new ZoneId(request.ToZoneId)));
    }

    private void HandleLeaveZone(SessionState session, LeaveZoneRequest request, List<WorldCommand> worldCommands)
    {
        if (session.EntityId is null)
        {
            return;
        }

        worldCommands.Add(new WorldCommand(
            Kind: WorldCommandKind.LeaveZone,
            EntityId: new EntityId(session.EntityId.Value),
            ZoneId: new ZoneId(request.ZoneId)));

        if (session.ActiveZoneId == request.ZoneId)
        {
            session.ActiveZoneId = null;
        }
    }

    private IEnumerable<SessionState> OrderedSessions() => _sessions.Values.OrderBy(s => s.SessionId.Value);

    private IEnumerable<PendingInput> OrderedPendingInputs(int targetTick)
    {
        List<PendingInput> due = new();

        foreach (SessionState session in OrderedSessions())
        {
            List<PendingInput> keep = new();

            foreach (PendingInput pending in session.PendingInputs)
            {
                if (pending.Tick <= targetTick)
                {
                    due.Add(pending);
                }
                else
                {
                    keep.Add(pending);
                }
            }

            session.PendingInputs.Clear();
            session.PendingInputs.AddRange(keep);
        }

        return due
            .OrderBy(input => input.Tick)
            .ThenBy(input => input.SessionId.Value)
            .ToArray();
    }


    private IEnumerable<PendingAttackIntent> OrderedPendingAttackIntents(int targetTick)
    {
        PendingAttackIntent[] due = _pendingAttackIntents
            .Where(p => p.Tick <= targetTick)
            .OrderBy(p => p.Tick)
            .ThenBy(p => p.SessionId.Value)
            .ThenBy(p => p.TargetEntityId)
            .ToArray();

        _pendingAttackIntents.RemoveAll(p => p.Tick <= targetTick);
        return due;
    }


    private void SynchronizeSessionZonesWithWorld()
    {
        foreach (SessionState session in OrderedSessions())
        {
            if (session.EntityId is null)
            {
                continue;
            }

            if (_world.TryGetEntityZone(new EntityId(session.EntityId.Value), out ZoneId zoneId))
            {
                session.ActiveZoneId = zoneId.Value;
                if (session.PlayerId is PlayerId playerId)
                {
                    _playerRegistry.UpdateWorldState(playerId, session.EntityId.Value, zoneId.Value, isAlive: true);
                }
            }
            else if (session.PlayerId is PlayerId playerId)
            {
                _playerRegistry.UpdateWorldState(playerId, null, null, isAlive: false);
                session.EntityId = null;
                session.ActiveZoneId = null;
            }
        }
    }

    private void EmitSnapshots()
    {
        int emittedCount = 0;

        foreach (SessionState session in OrderedSessions())
        {
            if (session.ActiveZoneId is null)
            {
                continue;
            }

            ZoneId zoneId = new(session.ActiveZoneId.Value);
            if (!_world.TryGetZone(zoneId, out ZoneState zone))
            {
                continue;
            }

            EntityState? self = session.EntityId is int selfEntityId
                ? zone.Entities.FirstOrDefault(entity => entity.Id.Value == selfEntityId)
                : null;

            SnapshotEntity[] entities;
            if (self is null)
            {
                entities = Array.Empty<SnapshotEntity>();
            }
            else
            {
                Vec2Fix selfPos = self.Pos;
                entities = zone.Entities
                    .OrderBy(entity => entity.Id.Value)
                    .Where(entity => entity.Id == self.Id || IsVisible(selfPos, entity.Pos))
                    .Select(entity => new SnapshotEntity(
                        EntityId: entity.Id.Value,
                        PosXRaw: entity.Pos.X.Raw,
                        PosYRaw: entity.Pos.Y.Raw,
                        VelXRaw: entity.Vel.X.Raw,
                        VelYRaw: entity.Vel.Y.Raw,
                        Hp: entity.Hp,
                        Kind: entity.Kind switch
                        {
                            Game.Core.EntityKind.Player => SnapshotEntityKind.Player,
                            Game.Core.EntityKind.Npc => SnapshotEntityKind.Npc,
                            _ => SnapshotEntityKind.Unknown
                        }))
                    .ToArray();
            }

            Snapshot snapshot = new(
                Tick: _world.Tick,
                ZoneId: zone.Id.Value,
                Entities: entities);

            if (self is not null && !snapshot.Entities.Any(e => e.EntityId == self.Id.Value))
            {
                throw new InvariantViolationException($"invariant=SnapshotIncludesSelf tick={_world.Tick} zoneId={zone.Id.Value} sessionId={session.SessionId.Value} entityId={self.Id.Value}");
            }

            session.LastSnapshotTick = _world.Tick;
            session.Endpoint.EnqueueToClient(ProtocolCodec.Encode(snapshot));
            Metrics.IncrementMessagesOut();
            emittedCount++;
            _recentSnapshots.Add(snapshot);
        }

        if (emittedCount > 0)
        {
            _logger.LogInformation(ServerLogEvents.SnapshotEmitted, "SnapshotEmitted tick={Tick} sessions={Sessions}", _world.Tick, emittedCount);
        }
    }

    private bool IsVisible(Vec2Fix observerPos, Vec2Fix targetPos)
    {
        Vec2Fix delta = targetPos - observerPos;
        Fix32 distSq = delta.LengthSq();
        return distSq <= _serverConfig.VisionRadiusSq;
    }

    private void DisconnectSession(SessionState session, string reason)
    {
        if (_sessions.Remove(session.SessionId.Value))
        {
            if (session.PlayerId is PlayerId playerId)
            {
                _playerRegistry.DetachSession(session.SessionId);
                if (_playerRegistry.TryGetState(playerId, out PlayerState? state))
                {
                    _playerRegistry.UpdateWorldState(playerId, state.EntityId, state.ZoneId, state.IsAlive);
                }
            }

            session.Endpoint.Close();
            Metrics.SetPlayersConnected(_sessions.Count);
            _logger.LogInformation(ServerLogEvents.SessionDisconnected, "SessionDisconnected sessionId={SessionId} reason={Reason}", session.SessionId.Value, reason);
        }
    }

    private sealed class SessionState
    {
        public SessionState(SessionId sessionId, IServerEndpoint endpoint)
        {
            SessionId = sessionId;
            Endpoint = endpoint;
        }

        public SessionId SessionId { get; }

        public IServerEndpoint Endpoint { get; }

        public string? AccountId { get; set; }

        public PlayerId? PlayerId { get; set; }

        public int? EntityId { get; set; }

        public int? ActiveZoneId { get; set; }

        public List<PendingInput> PendingInputs { get; } = new();

        public int LastSnapshotTick { get; set; } = -1;
    }

    private readonly record struct PendingInput(int Tick, SessionId SessionId, sbyte MoveX, sbyte MoveY);

    private readonly record struct PendingAttackIntent(int Tick, SessionId SessionId, int ZoneId, int TargetEntityId);
}
