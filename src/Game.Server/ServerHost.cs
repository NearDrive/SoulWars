using System.Collections.Immutable;
using Game.Audit;
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
    private readonly IAuditSink _auditSink;
    private readonly IAoiProvider _aoiProvider;
    private readonly SimulationInstrumentation _simulationInstrumentation;
    private readonly DenyList _denyList = new();
    private readonly Dictionary<string, Queue<int>> _abuseDisconnectTicksByEndpoint = new(StringComparer.Ordinal);
    private readonly ZoneDefinitions? _zoneDefinitions;

    private int _nextSessionId = 1;
    private int _nextEntityId = 1;
    private readonly List<WorldCommand> _pendingWorldCommands = new();
    private readonly List<PendingAttackIntent> _pendingAttackIntents = new();
    private readonly List<Snapshot> _recentSnapshots = new();
    private WorldState _world;
    private int _lastTick;
    private int _auditSeq;

    public ServerHost(ServerConfig config, ILoggerFactory? loggerFactory = null, ServerMetrics? metrics = null, ServerBootstrap? bootstrap = null, IAuditSink? auditSink = null)
    {
        if (config.SnapshotEveryTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "SnapshotEveryTicks must be > 0.");
        }

        _serverConfig = config;
        _simulationConfig = config.ToSimulationConfig();
        _zoneDefinitions = string.IsNullOrWhiteSpace(config.ZoneDefinitionsPath)
            ? null
            : ZoneDefinitionsLoader.LoadFromDirectory(config.ZoneDefinitionsPath);
        _world = bootstrap?.World ?? Simulation.CreateInitialState(_simulationConfig, _zoneDefinitions);
        _nextEntityId = Math.Max(_nextEntityId, ComputeNextEntityId(_world));
        if (bootstrap is not null)
        {
            _playerRegistry.LoadFromRecords(bootstrap.Players);
        }

        if (_zoneDefinitions is not null)
        {
            ServerInvariants.ValidateManualZoneDefinitions(_world, _zoneDefinitions, _world.Tick);
        }
        ILoggerFactory factory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = factory.CreateLogger<ServerHost>();
        bool enableMetrics = config.EnableMetrics;
        Metrics = metrics ?? new ServerMetrics(enableMetrics);
        _auditSink = auditSink ?? NullAuditSink.Instance;
        _aoiProvider = new RadiusAoiProvider(_serverConfig.AoiRadiusSq);
        Perf = new PerfCounters();
        _simulationInstrumentation = new SimulationInstrumentation
        {
            CountEntitiesVisited = Perf.CountEntitiesVisited,
            CountCollisionChecks = Perf.CountCollisionChecks
        };
        _lastTick = _world.Tick;
    }


    public WorldState CurrentWorld => _world;

    public int Seed => _serverConfig.Seed;

    public IReadOnlyList<PlayerState> GetPlayersSnapshot() => _playerRegistry.OrderedStates().ToArray();
    public ServerMetrics Metrics { get; }

    public PerfCounters Perf { get; }

    public bool TryConnect(IServerEndpoint endpoint, out SessionId sessionId, out DisconnectReason? denyReason)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        _denyList.CleanupExpired(_world.Tick);
        if (_denyList.IsDenied(endpoint.EndpointKey, _world.Tick))
        {
            _logger.LogWarning(ServerLogEvents.AbuseDisconnect, "ConnectionRejected tick={Tick} endpoint={Endpoint} reason={Reason}", _world.Tick, endpoint.EndpointKey, DisconnectReason.DenyListed);
            SendDisconnectAndCloseEndpoint(endpoint, DisconnectReason.DenyListed);
            denyReason = DisconnectReason.DenyListed;
            sessionId = default;
            return false;
        }

        if (_sessions.Count >= _serverConfig.MaxConcurrentSessions ||
            CountActiveSessionsForEndpoint(endpoint.EndpointKey) >= _serverConfig.MaxConnectionsPerIp)
        {
            _logger.LogWarning(ServerLogEvents.AbuseDisconnect, "ConnectionRejected tick={Tick} endpoint={Endpoint} reason={Reason}", _world.Tick, endpoint.EndpointKey, DisconnectReason.ConnLimitExceeded);
            SendDisconnectAndCloseEndpoint(endpoint, DisconnectReason.ConnLimitExceeded);
            denyReason = DisconnectReason.ConnLimitExceeded;
            sessionId = default;
            return false;
        }

        sessionId = new SessionId(_nextSessionId++);
        SessionState session = new(sessionId, endpoint);
        _sessions[sessionId.Value] = session;
        Metrics.SetPlayersConnected(_sessions.Count);

        _logger.LogInformation(ServerLogEvents.SessionConnected, "SessionConnected sessionId={SessionId}", sessionId.Value);
        denyReason = null;
        return true;
    }

    public SessionId Connect(IServerEndpoint endpoint)
    {
        if (!TryConnect(endpoint, out SessionId sessionId, out DisconnectReason? reason))
        {
            throw new InvalidOperationException($"Connection rejected: {reason}");
        }

        return sessionId;
    }


    public void SaveToSqlite(string dbPath)
    {
        ServerPersistence persistence = new();
        persistence.Save(this, dbPath);
    }

    public static ServerHost LoadFromSqlite(ServerConfig config, string dbPath, ILoggerFactory? loggerFactory = null, ServerMetrics? metrics = null)
    {
        ServerPersistence persistence = new();
        ServerBootstrap bootstrap = persistence.Load(dbPath);
        ServerConfig loadedConfig = config with { Seed = bootstrap.ServerSeed };
        return new ServerHost(loadedConfig, loggerFactory, metrics, bootstrap);
    }
    public void StepOnce()
    {
        Perf.ResetTick();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long messagesInBefore = Metrics.MessagesIn;
        long messagesOutBefore = Metrics.MessagesOut;

        ProcessInboundOnce();
        AdvanceSimulationOnce();

        double simStepMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        long deltaIn = Metrics.MessagesIn - messagesInBefore;
        long deltaOut = Metrics.MessagesOut - messagesOutBefore;
        int messagesIn = (int)Math.Clamp(deltaIn, 0, int.MaxValue);
        int messagesOut = (int)Math.Clamp(deltaOut, 0, int.MaxValue);
        int sessionCount = _sessions.Count;

        Metrics.OnTickCompleted(_world.Tick, simStepMs, messagesIn, messagesOut, sessionCount);
        Perf.CompleteTick();
        if (_serverConfig.EnableStructuredLogs)
        {
            string json = LogJson.TickEntry(_world.Tick, sessionCount, messagesIn, messagesOut, simStepMs);
            _logger.LogInformation("{Json}", json);
        }
    }

    public void ProcessInboundOnce()
    {
        int targetTick = _world.Tick + 1;
        _denyList.CleanupExpired(_world.Tick);

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
        HashSet<int> beforeEntityIds = _world.Zones
            .SelectMany(zone => zone.Entities)
            .Select(entity => entity.Id.Value)
            .ToHashSet();

        foreach (PendingInput pending in OrderedPendingInputs(targetTick))
        {
            worldCommands.Add(new WorldCommand(
                Kind: WorldCommandKind.MoveIntent,
                EntityId: new EntityId(pending.EntityId),
                ZoneId: new ZoneId(pending.ZoneId),
                MoveX: pending.MoveX,
                MoveY: pending.MoveY));
        }

        foreach (PendingAttackIntent pending in OrderedPendingAttackIntents(targetTick))
        {
            worldCommands.Add(new WorldCommand(
                Kind: WorldCommandKind.AttackIntent,
                EntityId: new EntityId(pending.EntityId),
                ZoneId: new ZoneId(pending.ZoneId),
                TargetEntityId: new EntityId(pending.TargetEntityId)));
        }

        _world = Simulation.Step(_simulationConfig, _world, new Inputs(worldCommands.ToImmutableArray()), _simulationInstrumentation);
        HashSet<int> afterSimEntityIds = _world.Zones
            .SelectMany(zone => zone.Entities)
            .Select(entity => entity.Id.Value)
            .ToHashSet();

        HashSet<int> leaveEntityIds = worldCommands
            .Where(command => command.Kind == WorldCommandKind.LeaveZone)
            .Select(command => command.EntityId.Value)
            .ToHashSet();

        foreach (int removedEntityId in beforeEntityIds.Except(afterSimEntityIds).OrderBy(id => id))
        {
            if (leaveEntityIds.Contains(removedEntityId))
            {
                continue;
            }

            _auditSink.Emit(AuditEvent.Death(_world.Tick, NextAuditSeq(), removedEntityId, killerEntityId: null));
        }

        ProcessDisconnectedPlayerCleanup(_world.Tick);
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
                NextSessionId: _nextSessionId,
                NextEntityId: _nextEntityId,
                Sessions: OrderedSessions().Select(s => new ServerSessionDebugView(s.SessionId.Value, s.EntityId, s.LastSnapshotTick, s.CurrentZoneId)).ToArray(),
                Players: _playerRegistry.OrderedStates().ToArray(),
                Snapshots: _recentSnapshots.ToArray(),
                World: _world));
        }

        if (_zoneDefinitions is not null)
        {
            ServerInvariants.ValidateManualZoneDefinitions(_world, _zoneDefinitions, _world.Tick);
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

    public MetricsSnapshot SnapshotMetrics() => Metrics.Snapshot(_serverConfig.TickHz);

    public bool WorldContainsEntity(int entityId)
    {
        return _world.TryGetEntityZone(new EntityId(entityId), out _);
    }

    public int CountWorldEntitiesForPlayer(PlayerId playerId)
    {
        if (!_playerRegistry.TryGetState(playerId, out PlayerState state) || state.EntityId is null)
        {
            return 0;
        }

        int id = state.EntityId.Value;
        int count = 0;
        foreach (ZoneState zone in _world.Zones)
        {
            count += zone.Entities.Count(entity => entity.Id.Value == id);
        }

        return count;
    }

    public bool TryGetPlayerState(PlayerId playerId, out PlayerState state) => _playerRegistry.TryGetState(playerId, out state);

    public int ActiveSessionCount => _sessions.Count;

    public int PendingWorldCommandCount => _pendingWorldCommands.Count;

    public int PendingAttackIntentCount => _pendingAttackIntents.Count;

    public int WorldEntityCountTotal => _world.Zones.Sum(zone => zone.Entities.Length);

    private void DrainSessionMessages(SessionState session, int targetTick, List<WorldCommand> worldCommands)
    {
        while (session.Endpoint.TryDequeueToServer(out byte[] payload))
        {
            ResetSessionRateCountersForTick(session, targetTick);
            session.MsgsThisTick++;
            session.BytesThisTick += payload.Length;
            if (session.MsgsThisTick > _serverConfig.MaxMsgsPerTick || session.BytesThisTick > _serverConfig.MaxBytesPerTick)
            {
                DisconnectWithReason(session, DisconnectReason.RateLimitExceeded);
                break;
            }

            if (payload.Length > _serverConfig.MaxPayloadBytes)
            {
                _logger.LogWarning(ServerLogEvents.OversizedMessage, "OversizedMessage sessionId={SessionId} payloadBytes={PayloadBytes}", session.SessionId.Value, payload.Length);
                DisconnectWithReason(session, DisconnectReason.PayloadTooLarge);
                break;
            }

            IClientMessage? message;
            ProtocolErrorCode error;
            bool decoded;
            try
            {
                decoded = ProtocolCodec.TryDecodeClient(payload, out message, out error);
            }
            catch (Exception ex)
            {
                Metrics.IncrementProtocolDecodeErrors();
                _logger.LogWarning(ServerLogEvents.ProtocolDecodeFailed, ex, "ProtocolDecodeFailed sessionId={SessionId} error={Error}", session.SessionId.Value, "exception");
                DisconnectWithReason(session, DisconnectReason.DecodeError);
                break;
            }

            if (!decoded)
            {
                Metrics.IncrementProtocolDecodeErrors();
                if (error == ProtocolErrorCode.UnknownMessageType)
                {
                    _logger.LogWarning(
                        ServerLogEvents.UnknownClientMessageType,
                        "UnknownClientMessageType sessionId={SessionId} messageTypeId={MessageTypeId}",
                        session.SessionId.Value,
                        payload.Length > 0 ? payload[0] : (byte)0);
                    continue;
                }

                _logger.LogWarning(ServerLogEvents.ProtocolDecodeFailed, "ProtocolDecodeFailed sessionId={SessionId} error={Error}", session.SessionId.Value, error);
                if (error is ProtocolErrorCode.Truncated or ProtocolErrorCode.InvalidLength)
                {
                    DisconnectWithReason(session, DisconnectReason.DecodeError);
                    break;
                }

                continue;
            }

            Metrics.IncrementMessagesIn();
            Perf.AddInboundMessages(1);
            Perf.AddInboundBytes(payload.Length);
            Perf.CountCommandsProcessed(1);

            switch (message)
            {
                case HandshakeRequest handshake:
                    HandleHandshake(session, handshake);
                    break;
                case Hello _:
                case HelloV2 _:
                    _logger.LogWarning(ServerLogEvents.UnknownClientMessageType, "UnknownClientMessageType sessionId={SessionId} reason={Reason}", session.SessionId.Value, "legacy_hello_unexpected_after_decode");
                    break;
                case EnterZoneRequest enterZoneRequest:
                    HandleEnterZone(session, enterZoneRequest.ZoneId, worldCommands);
                    break;
                case EnterZoneRequestV2 enterZoneRequestV2:
                    HandleEnterZone(session, enterZoneRequestV2.ZoneId, worldCommands);
                    break;
                case InputCommand inputCommand:
                    if (!TryEnqueueInputForTick(session, targetTick, inputCommand))
                    {
                        break;
                    }

                    break;
                case AttackIntent attackIntent:
                    PendingAttackIntent? pendingAttack = CreatePendingAttackIntent(targetTick, session, attackIntent);
                    if (pendingAttack is not null)
                    {
                        _pendingAttackIntents.Add(pendingAttack.Value);
                    }
                    break;
                case LeaveZoneRequest leaveZoneRequest:
                    HandleLeaveZone(session, leaveZoneRequest.ZoneId, worldCommands);
                    break;
                case LeaveZoneRequestV2 leaveZoneRequestV2:
                    HandleLeaveZone(session, leaveZoneRequestV2.ZoneId, worldCommands);
                    break;
                case TeleportRequest teleportRequest:
                    HandleTeleport(session, teleportRequest, worldCommands);
                    break;
                case ClientAckV2 ack:
                    HandleClientAck(session, ack);
                    break;
            }
        }
    }

    private bool TryEnqueueInputForTick(SessionState session, int targetTick, InputCommand inputCommand)
    {
        if (session.LastTickSeen != targetTick)
        {
            session.LastTickSeen = targetTick;
            session.InputsAcceptedThisTick = 0;
        }

        if (session.InputsAcceptedThisTick >= _serverConfig.MaxInputsPerTickPerSession)
        {
            return false;
        }

        if (session.EntityId is null || session.CurrentZoneId is null)
        {
            return false;
        }

        PendingInput pending = ClampMoveInput(targetTick, session.EntityId.Value, session.CurrentZoneId.Value, inputCommand.MoveX, inputCommand.MoveY);
        session.PendingInputs.Add(pending);
        session.InputsAcceptedThisTick++;
        return true;
    }

    private PendingAttackIntent? CreatePendingAttackIntent(int targetTick, SessionState session, AttackIntent attackIntent)
    {
        if (session.EntityId is null || session.CurrentZoneId is null)
        {
            return null;
        }

        if (session.CurrentZoneId.Value != attackIntent.ZoneId)
        {
            return null;
        }

        return new PendingAttackIntent(targetTick, session.EntityId.Value, session.CurrentZoneId.Value, attackIntent.TargetId);
    }

    private PendingInput ClampMoveInput(int tick, int entityId, int zoneId, sbyte moveX, sbyte moveY)
    {
        int maxAxis = Math.Clamp(Fix32.FloorToInt(_serverConfig.MaxMoveVectorLen), 0, 1);
        int clampedX = Math.Clamp(moveX, -maxAxis, maxAxis);
        int clampedY = Math.Clamp(moveY, -maxAxis, maxAxis);

        return new PendingInput(tick, entityId, zoneId, (sbyte)clampedX, (sbyte)clampedY);
    }

    private void HandleHandshake(SessionState session, HandshakeRequest request)
    {
        if (request.ProtocolVersion != ProtocolConstants.CurrentProtocolVersion)
        {
            DisconnectWithReason(session, DisconnectReason.VersionMismatch);
            return;
        }

        string normalizedAccountId = string.IsNullOrWhiteSpace(request.AccountId)
            ? $"anon-session-{session.SessionId.Value}"
            : request.AccountId.Trim();

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

        if (_playerRegistry.TryGetState(playerId, out PlayerState state))
        {
            session.EntityId = state.EntityId;
            session.CurrentZoneId = state.ZoneId;
        }

        EnqueueToClient(session, ProtocolCodec.Encode(new Welcome(session.SessionId, playerId, ProtocolConstants.CurrentProtocolVersion, ProtocolConstants.ServerCapabilities)));
        Metrics.IncrementMessagesOut();

        _auditSink.Emit(AuditEvent.PlayerConnected(_world.Tick, NextAuditSeq(), normalizedAccountId, playerId.Value));
    }

    private void HandleEnterZone(SessionState session, int requestedZoneId, List<WorldCommand> worldCommands)
    {
        if (session.PlayerId is null)
        {
            return;
        }

        if (!_playerRegistry.TryGetState(session.PlayerId.Value, out PlayerState playerState))
        {
            return;
        }

        if (session.EntityId is int pendingLeaveEntityId &&
            HasPendingLeaveCommand(worldCommands, pendingLeaveEntityId) &&
            _world.TryGetEntityZone(new EntityId(pendingLeaveEntityId), out ZoneId pendingLeaveZoneId))
        {
            RemovePendingLeaveCommandsForEntity(worldCommands, pendingLeaveEntityId);
            session.CurrentZoneId = pendingLeaveZoneId.Value;
            _playerRegistry.UpdateWorldState(session.PlayerId.Value, pendingLeaveEntityId, pendingLeaveZoneId.Value, isAlive: true);
            EnqueueToClient(session, ProtocolCodec.Encode(new EnterZoneAck(pendingLeaveZoneId.Value, pendingLeaveEntityId)));
            Metrics.IncrementMessagesOut();
            return;
        }

        if (session.CurrentZoneId == requestedZoneId &&
            session.EntityId is int currentEntityId &&
            !HasPendingLeaveCommand(worldCommands, currentEntityId) &&
            _world.TryGetEntityZone(new EntityId(currentEntityId), out ZoneId currentZoneId) &&
            currentZoneId.Value == requestedZoneId)
        {
            EnqueueToClient(session, ProtocolCodec.Encode(new EnterZoneAck(requestedZoneId, currentEntityId)));
            Metrics.IncrementMessagesOut();
            return;
        }

        int zoneId = playerState.ZoneId ?? requestedZoneId;
        int entityId;

        if (_zoneDefinitions is not null && !_zoneDefinitions.TryGetZone(new ZoneId(zoneId), out _))
        {
            throw new InvalidOperationException($"Zone {zoneId} is not defined in manual zone definitions.");
        }

        if (playerState.EntityId is int existingEntityId &&
            !HasPendingLeaveCommand(worldCommands, existingEntityId) &&
            _world.TryGetEntityZone(new EntityId(existingEntityId), out ZoneId existingZoneId))
        {
            entityId = existingEntityId;
            zoneId = existingZoneId.Value;
        }
        else
        {
            entityId = playerState.EntityId ?? _nextEntityId++;
            worldCommands.Add(new WorldCommand(
                Kind: WorldCommandKind.EnterZone,
                EntityId: new EntityId(entityId),
                ZoneId: new ZoneId(zoneId)));
        }

        session.EntityId = entityId;
        session.CurrentZoneId = zoneId;
        _playerRegistry.UpdateWorldState(session.PlayerId.Value, entityId, zoneId, isAlive: true);

        EnqueueToClient(session, ProtocolCodec.Encode(new EnterZoneAck(zoneId, entityId)));
        Metrics.IncrementMessagesOut();

        _logger.LogInformation(
            ServerLogEvents.SessionEnteredZone,
            "SessionEnteredZone sessionId={SessionId} playerId={PlayerId} zoneId={ZoneId} entityId={EntityId}",
            session.SessionId.Value,
            session.PlayerId.Value.Value,
            zoneId,
            entityId);

        _auditSink.Emit(AuditEvent.EnterZone(_world.Tick + 1, NextAuditSeq(), session.PlayerId.Value.Value, zoneId, entityId));
    }

    private static bool HasPendingLeaveCommand(List<WorldCommand> worldCommands, int entityId)
    {
        return worldCommands.Any(command =>
            command.Kind == WorldCommandKind.LeaveZone &&
            command.EntityId.Value == entityId);
    }

    private static void RemovePendingLeaveCommandsForEntity(List<WorldCommand> worldCommands, int entityId)
    {
        worldCommands.RemoveAll(command =>
            command.Kind == WorldCommandKind.LeaveZone &&
            command.EntityId.Value == entityId);
    }



    private void HandleClientAck(SessionState session, ClientAckV2 ack)
    {
        if (ack.ZoneId <= 0 || ack.LastSnapshotSeqReceived < 0)
        {
            return;
        }

        session.SupportsSnapshotAckV2 = true;

        if (ack.LastSnapshotSeqReceived <= session.LastAckedSnapshotSeq)
        {
            return;
        }

        int latestSentSeq = session.NextSnapshotSeq - 1;
        if (ack.LastSnapshotSeqReceived > latestSentSeq)
        {
            _logger.LogWarning(
                ServerLogEvents.InvalidClientAck,
                "InvalidClientAck sessionId={SessionId} zoneId={ZoneId} ackSeq={AckSeq} latestSentSeq={LatestSentSeq}",
                session.SessionId.Value,
                ack.ZoneId,
                ack.LastSnapshotSeqReceived,
                latestSentSeq);
            return;
        }

        session.LastAckedSnapshotSeq = ack.LastSnapshotSeqReceived;
        if (session.LastFullSnapshot is { } last && last.SnapshotSeq == ack.LastSnapshotSeqReceived)
        {
            session.LastSnapshotRetryCount = 0;
        }
    }

    private void HandleTeleport(SessionState session, TeleportRequest request, List<WorldCommand> worldCommands)
    {
        if (session.EntityId is null || session.CurrentZoneId is null)
        {
            return;
        }

        int fromZoneId = session.CurrentZoneId.Value;
        if (request.ToZoneId <= 0 || request.ToZoneId > _simulationConfig.ZoneCount || request.ToZoneId == fromZoneId)
        {
            return;
        }

        worldCommands.Add(new WorldCommand(
            Kind: WorldCommandKind.TeleportIntent,
            EntityId: new EntityId(session.EntityId.Value),
            ZoneId: new ZoneId(fromZoneId),
            ToZoneId: new ZoneId(request.ToZoneId)));

        if (session.PlayerId is PlayerId playerId)
        {
            _auditSink.Emit(AuditEvent.Teleport(_world.Tick + 1, NextAuditSeq(), playerId.Value, fromZoneId, request.ToZoneId, session.EntityId.Value));
        }
    }

    private void HandleLeaveZone(SessionState session, int requestedZoneId, List<WorldCommand> worldCommands)
    {
        if (session.EntityId is null || session.CurrentZoneId is null)
        {
            return;
        }

        if (requestedZoneId != session.CurrentZoneId.Value)
        {
            return;
        }

        worldCommands.Add(new WorldCommand(
            Kind: WorldCommandKind.LeaveZone,
            EntityId: new EntityId(session.EntityId.Value),
            ZoneId: new ZoneId(requestedZoneId)));

        session.CurrentZoneId = null;
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
            .ThenBy(input => input.ZoneId)
            .ThenBy(input => input.EntityId)
            .ToArray();
    }


    private IEnumerable<PendingAttackIntent> OrderedPendingAttackIntents(int targetTick)
    {
        PendingAttackIntent[] due = _pendingAttackIntents
            .Where(p => p.Tick <= targetTick)
            .OrderBy(p => p.Tick)
            .ThenBy(p => p.ZoneId)
            .ThenBy(p => p.EntityId)
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
                session.CurrentZoneId = zoneId.Value;
                if (session.PlayerId is PlayerId playerId)
                {
                    _playerRegistry.UpdateWorldState(playerId, session.EntityId.Value, zoneId.Value, isAlive: true);
                }
            }
            else if (session.PlayerId is PlayerId playerId)
            {
                _playerRegistry.UpdateWorldState(playerId, null, session.CurrentZoneId, isAlive: false);
                session.EntityId = null;
            }
        }

        foreach (PlayerState playerState in _playerRegistry.OrderedStates())
        {
            if (playerState.IsConnected || playerState.EntityId is null)
            {
                continue;
            }

            if (!_world.TryGetEntityZone(new EntityId(playerState.EntityId.Value), out ZoneId zoneId))
            {
                _playerRegistry.UpdateWorldState(playerState.PlayerId, null, playerState.ZoneId, isAlive: false);
            }
            else
            {
                _playerRegistry.UpdateWorldState(playerState.PlayerId, playerState.EntityId.Value, zoneId.Value, isAlive: true);
            }
        }
    }

    private void ProcessDisconnectedPlayerCleanup(int tick)
    {
        foreach (PlayerState playerState in _playerRegistry.OrderedStates())
        {
            if (playerState.IsConnected || playerState.EntityId is null || playerState.DespawnAtTick is null)
            {
                continue;
            }

            if (playerState.DespawnAtTick.Value > tick)
            {
                continue;
            }

            EntityId entityId = new(playerState.EntityId.Value);
            if (_world.TryGetEntityZone(entityId, out ZoneId zoneId) && _world.TryGetZone(zoneId, out ZoneState zone))
            {
                ZoneState updatedZone = zone.WithEntities(zone.Entities
                    .Where(entity => entity.Id.Value != entityId.Value)
                    .OrderBy(entity => entity.Id.Value)
                    .ToImmutableArray());
                _world = _world.WithZoneUpdated(updatedZone).WithoutEntityLocation(entityId);
            }

            _playerRegistry.UpdateWorldState(playerState.PlayerId, null, null, isAlive: false);
            _playerRegistry.UpdateConnectionState(
                playerState.PlayerId,
                isConnected: false,
                attachedSessionId: null,
                disconnectedAtTick: playerState.DisconnectedAtTick,
                despawnAtTick: null);

            _auditSink.Emit(AuditEvent.DespawnDisconnected(tick, NextAuditSeq(), playerState.PlayerId.Value, entityId.Value));
        }
    }

    private void EmitSnapshots()
    {
        int emittedCount = 0;

        foreach (SessionState session in OrderedSessions())
        {
            if (session.CurrentZoneId is null)
            {
                continue;
            }

            ZoneId zoneId = new(session.CurrentZoneId.Value);
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
                VisibleSet visibleSet = _aoiProvider.ComputeVisible(_world, zoneId, self.Id, Perf);
                HashSet<int> visibleIds = visibleSet.EntityIds.Select(entityId => entityId.Value).ToHashSet();
                entities = zone.Entities
                    .Where(entity => visibleIds.Contains(entity.Id.Value))
                    .OrderBy(entity => entity.Id.Value)
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

            if (session.SupportsSnapshotAckV2 &&
                session.LastFullSnapshot is { } lastSnapshot &&
                lastSnapshot.ZoneId == zone.Id.Value &&
                session.LastAckedSnapshotSeq < lastSnapshot.SnapshotSeq)
            {
                EnqueueToClient(session, lastSnapshot.EncodedPayload);
                Metrics.IncrementMessagesOut();
                emittedCount++;
                session.LastSnapshotRetryCount++;

                if (session.LastSnapshotRetryCount > _serverConfig.SnapshotRetryLimit)
                {
                    DisconnectSession(session, "snapshot_retry_limit_exceeded");
                    continue;
                }

                _logger.LogInformation(
                    ServerLogEvents.SnapshotResent,
                    "SnapshotResent tick={Tick} sessionId={SessionId} zoneId={ZoneId} snapshotSeq={SnapshotSeq} retry={RetryCount}",
                    _world.Tick,
                    session.SessionId.Value,
                    zone.Id.Value,
                    lastSnapshot.SnapshotSeq,
                    session.LastSnapshotRetryCount);
            }

            if (session.SupportsSnapshotAckV2)
            {
                int snapshotSeq = session.NextSnapshotSeq++;
                SnapshotV2 snapshot = new(
                    Tick: _world.Tick,
                    ZoneId: zone.Id.Value,
                    SnapshotSeq: snapshotSeq,
                    IsFull: true,
                    Entities: entities);

                if (self is not null && !snapshot.Entities.Any(e => e.EntityId == self.Id.Value))
                {
                    throw new InvariantViolationException($"invariant=SnapshotIncludesSelf tick={_world.Tick} zoneId={zone.Id.Value} sessionId={session.SessionId.Value} entityId={self.Id.Value}");
                }

                Perf.CountSnapshotsEncodedEntities(snapshot.Entities.Length);
                byte[] encodedV2 = ProtocolCodec.Encode(snapshot);
                session.LastFullSnapshot = new SessionSnapshotCache(zone.Id.Value, snapshotSeq, encodedV2);

                session.LastSnapshotTick = _world.Tick;
                EnqueueToClient(session, encodedV2);
                Metrics.IncrementMessagesOut();
                emittedCount++;
                _recentSnapshots.Add(new Snapshot(snapshot.Tick, snapshot.ZoneId, snapshot.Entities));
                continue;
            }

            Snapshot legacySnapshot = new(
                Tick: _world.Tick,
                ZoneId: zone.Id.Value,
                Entities: entities);

            if (self is not null && !legacySnapshot.Entities.Any(e => e.EntityId == self.Id.Value))
            {
                throw new InvariantViolationException($"invariant=SnapshotIncludesSelf tick={_world.Tick} zoneId={zone.Id.Value} sessionId={session.SessionId.Value} entityId={self.Id.Value}");
            }

            session.LastSnapshotTick = _world.Tick;
            Perf.CountSnapshotsEncodedEntities(legacySnapshot.Entities.Length);
            EnqueueToClient(session, ProtocolCodec.Encode(legacySnapshot));
            Metrics.IncrementMessagesOut();
            emittedCount++;
            _recentSnapshots.Add(legacySnapshot);
        }

        if (emittedCount > 0)
        {
            _logger.LogInformation(ServerLogEvents.SnapshotEmitted, "SnapshotEmitted tick={Tick} sessions={Sessions}", _world.Tick, emittedCount);
        }
    }


    public PerfSnapshot SnapshotAndResetPerfWindow() => Perf.SnapshotAndResetWindow();

    private int CountActiveSessionsForEndpoint(string endpointKey)
    {
        return _sessions.Values.Count(s => string.Equals(s.Endpoint.EndpointKey, endpointKey, StringComparison.Ordinal));
    }

    private void SendDisconnectAndCloseEndpoint(IServerEndpoint endpoint, DisconnectReason reason)
    {
        try
        {
            endpoint.EnqueueToClient(ProtocolCodec.Encode(new Disconnect(reason)));
            Metrics.IncrementMessagesOut();
        }
        catch
        {
        }

        endpoint.Close();
    }

    private void EnqueueToClient(SessionState session, byte[] payload)
    {
        session.Endpoint.EnqueueToClient(payload);
        Perf.AddOutboundMessages(1);
        Perf.AddOutboundBytes(payload.Length);
    }

    private int NextAuditSeq() => ++_auditSeq;

    private void ResetSessionRateCountersForTick(SessionState session, int tick)
    {
        if (session.LastTick != tick)
        {
            session.LastTick = tick;
            session.MsgsThisTick = 0;
            session.BytesThisTick = 0;
        }
    }

    private void DisconnectWithReason(SessionState session, DisconnectReason reason)
    {
        EnqueueToClient(session, ProtocolCodec.Encode(new Disconnect(reason)));
        Metrics.IncrementMessagesOut();
        LogAbuseDisconnect(session, reason);

        if (reason is DisconnectReason.RateLimitExceeded or DisconnectReason.PayloadTooLarge or DisconnectReason.DecodeError or DisconnectReason.ProtocolViolation)
        {
            RegisterAbuseStrike(session.Endpoint.EndpointKey);
        }

        DisconnectSession(session, reason.ToString());
    }

    private void RegisterAbuseStrike(string endpointKey)
    {
        if (!_abuseDisconnectTicksByEndpoint.TryGetValue(endpointKey, out Queue<int>? strikes))
        {
            strikes = new Queue<int>();
            _abuseDisconnectTicksByEndpoint[endpointKey] = strikes;
        }

        strikes.Enqueue(_world.Tick);
        int minTick = _world.Tick - _serverConfig.AbuseWindowTicks;
        while (strikes.Count > 0 && strikes.Peek() < minTick)
        {
            strikes.Dequeue();
        }

        if (strikes.Count >= _serverConfig.AbuseStrikesToDeny)
        {
            _denyList.Deny(endpointKey, _world.Tick + _serverConfig.DenyTicks);
        }
    }

    private void LogAbuseDisconnect(SessionState session, DisconnectReason reason)
    {
        _logger.LogWarning(
            ServerLogEvents.AbuseDisconnect,
            "AbuseDisconnect tick={Tick} sessionId={SessionId} endpoint={Endpoint} reason={Reason} msgsThisTick={MsgsThisTick} bytesThisTick={BytesThisTick}",
            _world.Tick,
            session.SessionId.Value,
            session.Endpoint.EndpointKey,
            reason,
            session.MsgsThisTick,
            session.BytesThisTick);
    }

    private void DisconnectSession(SessionState session, string reason)
    {
        if (_sessions.Remove(session.SessionId.Value))
        {
            if (session.PlayerId is PlayerId playerId)
            {
                _playerRegistry.DetachSession(session.SessionId);
                _playerRegistry.UpdateConnectionState(
                    playerId,
                    isConnected: false,
                    attachedSessionId: null,
                    disconnectedAtTick: _world.Tick,
                    despawnAtTick: _world.Tick + _serverConfig.DisconnectGraceTicks);
            }

            session.Endpoint.Close();
            Metrics.SetPlayersConnected(_sessions.Count);
            _logger.LogInformation(ServerLogEvents.SessionDisconnected, "SessionDisconnected sessionId={SessionId} reason={Reason}", session.SessionId.Value, reason);
        }
    }


    private static int ComputeNextEntityId(WorldState world)
    {
        int maxEntityId = 0;
        foreach (ZoneState zone in world.Zones)
        {
            foreach (EntityState entity in zone.Entities)
            {
                maxEntityId = Math.Max(maxEntityId, entity.Id.Value);
            }
        }

        return maxEntityId + 1;
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

        public int? CurrentZoneId { get; set; }

        public List<PendingInput> PendingInputs { get; } = new();

        public int LastTickSeen { get; set; } = -1;

        public int InputsAcceptedThisTick { get; set; }

        public int LastTick { get; set; } = -1;

        public int MsgsThisTick { get; set; }

        public int BytesThisTick { get; set; }

        public int LastSnapshotTick { get; set; } = -1;

        public bool SupportsSnapshotAckV2 { get; set; }

        public int NextSnapshotSeq { get; set; } = 1;

        public int LastAckedSnapshotSeq { get; set; }

        public SessionSnapshotCache? LastFullSnapshot { get; set; }

        public int LastSnapshotRetryCount { get; set; }
    }

    private readonly record struct SessionSnapshotCache(int ZoneId, int SnapshotSeq, byte[] EncodedPayload);

    private readonly record struct PendingInput(int Tick, int EntityId, int ZoneId, sbyte MoveX, sbyte MoveY);

    private readonly record struct PendingAttackIntent(int Tick, int EntityId, int ZoneId, int TargetEntityId);
}
