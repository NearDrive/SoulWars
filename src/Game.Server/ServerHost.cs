using System.Collections.Immutable;
using Game.Core;
using Game.Protocol;

namespace Game.Server;

public sealed class ServerHost
{
    private readonly SimulationConfig _simulationConfig;
    private readonly ServerConfig _serverConfig;
    private readonly Dictionary<int, SessionState> _sessions = new();

    private int _nextSessionId = 1;
    private int _nextEntityId = 1;
    private WorldState _world;

    public ServerHost(ServerConfig config)
    {
        if (config.SnapshotEveryTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "SnapshotEveryTicks must be > 0.");
        }

        _serverConfig = config;
        _simulationConfig = config.ToSimulationConfig();
        _world = Simulation.CreateInitialState(_simulationConfig);
    }

    public SessionId Connect(InMemoryEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        SessionId sessionId = new(_nextSessionId++);
        SessionState session = new(sessionId, endpoint);
        _sessions[sessionId.Value] = session;

        endpoint.EnqueueToClient(ProtocolCodec.Encode(new Welcome(sessionId)));

        return sessionId;
    }

    public void StepOnce()
    {
        int targetTick = _world.Tick + 1;
        List<WorldCommand> worldCommands = new();

        foreach (SessionState session in OrderedSessions())
        {
            DrainSessionMessages(session, targetTick, worldCommands);
        }

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

        _world = Simulation.Step(_simulationConfig, _world, new Inputs(worldCommands.ToImmutableArray()));

        if (_world.Tick % _serverConfig.SnapshotEveryTicks == 0)
        {
            EmitSnapshots();
        }
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

    private void DrainSessionMessages(SessionState session, int targetTick, List<WorldCommand> worldCommands)
    {
        while (session.Endpoint.TryDequeueToServer(out byte[] payload))
        {
            IClientMessage message = ProtocolCodec.DecodeClient(payload);

            switch (message)
            {
                case Hello:
                    break;
                case EnterZoneRequest enterZoneRequest:
                    HandleEnterZone(session, enterZoneRequest, worldCommands);
                    break;
                case InputCommand inputCommand:
                    int normalizedTick = Math.Max(targetTick, inputCommand.Tick);
                    session.PendingInputs.Add(new PendingInput(normalizedTick, session.SessionId, inputCommand.MoveX, inputCommand.MoveY));
                    break;
                case LeaveZoneRequest leaveZoneRequest:
                    HandleLeaveZone(session, leaveZoneRequest, worldCommands);
                    break;
            }
        }
    }

    private void HandleEnterZone(SessionState session, EnterZoneRequest request, List<WorldCommand> worldCommands)
    {
        if (session.EntityId is null)
        {
            session.EntityId = _nextEntityId++;
        }

        session.ActiveZoneId = request.ZoneId;

        worldCommands.Add(new WorldCommand(
            Kind: WorldCommandKind.EnterZone,
            EntityId: new EntityId(session.EntityId.Value),
            ZoneId: new ZoneId(request.ZoneId)));

        session.Endpoint.EnqueueToClient(ProtocolCodec.Encode(new EnterZoneAck(request.ZoneId, session.EntityId.Value)));
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

    private void EmitSnapshots()
    {
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

            SnapshotEntity[] entities = zone.Entities
                .OrderBy(entity => entity.Id.Value)
                .Select(entity => new SnapshotEntity(
                    EntityId: entity.Id.Value,
                    PosXRaw: entity.Pos.X.Raw,
                    PosYRaw: entity.Pos.Y.Raw,
                    VelXRaw: entity.Vel.X.Raw,
                    VelYRaw: entity.Vel.Y.Raw))
                .ToArray();

            Snapshot snapshot = new(
                Tick: _world.Tick,
                ZoneId: zone.Id.Value,
                Entities: entities);

            session.Endpoint.EnqueueToClient(ProtocolCodec.Encode(snapshot));
        }
    }

    private sealed class SessionState
    {
        public SessionState(SessionId sessionId, InMemoryEndpoint endpoint)
        {
            SessionId = sessionId;
            Endpoint = endpoint;
        }

        public SessionId SessionId { get; }

        public InMemoryEndpoint Endpoint { get; }

        public int? EntityId { get; set; }

        public int? ActiveZoneId { get; set; }

        public List<PendingInput> PendingInputs { get; } = new();
    }

    private readonly record struct PendingInput(int Tick, SessionId SessionId, sbyte MoveX, sbyte MoveY);
}
