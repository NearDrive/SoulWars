using Game.Client.Headless;
using Game.Client.Headless.Runtime;
using Game.Client.Headless.Transport;
using Game.Core;
using Game.Protocol;
using Game.Server;
using System.Collections.Immutable;
using System.Buffers.Binary;
using Xunit;

namespace Game.Server.Tests.ClientSmoke;

public sealed class ClientServerGoldenSmokeTests
{
    private const int RunTicks = 3600;
    private const string GoldenHash = "D2B4960A066F535DBDB9BCF9CF63E0070273D51DFCC44FB86837F128C89F0728";

    [Fact]
    [Trait("Category", "PR98")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public async Task ClientServer_Smoke60s_Arena_Golden()
    {
        (ClientRunResult result, SmokeDiagnostics diagnostics) = await RunArenaGoldenSmokeAsync(RunTicks);

        Assert.True(result.HandshakeOk, diagnostics.ToDeterministicReport(result));
        Assert.True(result.TicksProcessed >= RunTicks, $"Expected at least {RunTicks} ticks, got {result.TicksProcessed}.\n{diagnostics.ToDeterministicReport(result)}");
        Assert.True(result.HitEventsSeen > 0, $"Expected at least one HitEvent during the deterministic smoke run.\n{diagnostics.ToDeterministicReport(result)}");
        Assert.False(string.IsNullOrWhiteSpace(result.TraceHash), diagnostics.ToDeterministicReport(result));

        Assert.True(
            string.Equals(result.TraceHash, GoldenHash, StringComparison.Ordinal),
            $"TraceHash mismatch. Expected GoldenHash='{GoldenHash}', Actual TraceHash='{result.TraceHash}'. " +
            "If this change is intentional, follow docs/testing/golden-hash-update.md (dedicated golden-update commit/PR, then rerun canary).");
    }

    [Fact]
    [Trait("Category", "PR98")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public async Task ClientSmoke_CastPoint_IsAccepted_AndProducesHitEvent_InArenaFixture()
    {
        (ClientRunResult result, SmokeDiagnostics diagnostics) = await RunArenaGoldenSmokeAsync(runTicks: 240);

        Assert.True(diagnostics.HasAbility(1), diagnostics.ToDeterministicReport(result));
        Assert.True(result.HandshakeOk, diagnostics.ToDeterministicReport(result));
        Assert.True(diagnostics.CastAttempts > 0, diagnostics.ToDeterministicReport(result));
        Assert.True(diagnostics.CastAccepted > 0, diagnostics.ToDeterministicReport(result));
        Assert.True(result.HitEventsSeen > 0, diagnostics.ToDeterministicReport(result));
    }

    [Fact]
    [Trait("Category", "PR98")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public async Task ClientSmoke_KnowsSelfEntity_AfterEnterAndFirstSnapshot()
    {
        (ClientRunResult result, SmokeDiagnostics diagnostics) = await RunArenaGoldenSmokeAsync(runTicks: 64);

        Assert.True(result.HandshakeOk, diagnostics.ToDeterministicReport(result));
        Assert.True(diagnostics.SelfEntityId.HasValue, diagnostics.ToDeterministicReport(result));
        Assert.True(diagnostics.FirstSelfSeenTick.HasValue, diagnostics.ToDeterministicReport(result));
    }

    private static async Task<(ClientRunResult Result, SmokeDiagnostics Diagnostics)> RunArenaGoldenSmokeAsync(int runTicks)
    {
        ServerConfig config = ServerConfig.Default(seed: 9801) with
        {
            SnapshotEveryTicks = 1,
            ArenaMode = true,
            VisionRadius = Fix32.FromInt(8),
            VisionRadiusSq = Fix32.FromInt(8) * Fix32.FromInt(8)
        };

        ServerBootstrap bootstrap = CreateDeterministicArenaBootstrap(config);
        ServerHost host = new(config, bootstrap: bootstrap);

        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        ClientOptions options = new("inproc", 0, 1, "basic", ArenaZoneFactory.ArenaZoneId, 1, "client-smoke-pr98", StopOnFirstHit: false);
        await using InMemoryClientTransport innerTransport = new(endpoint);
        SmokeDiagnostics diagnostics = new();
        diagnostics.RecordAvailableAbilities(config.ToSimulationConfig().SkillDefinitions);
        host.CastDiagnosticsSink = diagnostics.RecordServerCastDiagnostics;
        await using InstrumentedClientTransport transport = new(innerTransport, diagnostics);
        HeadlessClientRunner runner = new(transport, options);

        using CancellationTokenSource cts = new();
        Task<ClientRunResult> runTask = runner.RunAsync(maxTicks: runTicks, cts.Token);

        const int maxServerSteps = 20000;
        int serverSteps = 0;

        while (!runTask.IsCompleted)
        {
            if (serverSteps++ >= maxServerSteps)
            {
                cts.Cancel();
                throw new Xunit.Sdk.XunitException($"Client run did not complete within {maxServerSteps} deterministic server steps.");
            }

            host.ProcessInboundOnce();
            host.AdvanceSimulationOnce();
            await DrainClientOutboundQueueAsync(endpoint, runTask);

            const int maxInboundDrainSpins = 4096;
            int inboundDrainSpins = 0;
            while (!runTask.IsCompleted && endpoint.PendingToServerCount > 0)
            {
                if (inboundDrainSpins++ >= maxInboundDrainSpins)
                {
                    break;
                }

                host.ProcessInboundOnce();
                await DrainClientOutboundQueueAsync(endpoint, runTask);
            }

            await Task.Yield();
        }

        ClientRunResult result = await runTask;
        return (result, diagnostics);
    }

    private static async Task DrainClientOutboundQueueAsync(InMemoryEndpoint endpoint, Task<ClientRunResult> runTask)
    {
        const int maxDrainSpins = 4096;
        int spins = 0;
        while (!runTask.IsCompleted && endpoint.PendingToClientCount > 0)
        {
            if (spins++ >= maxDrainSpins)
            {
                break;
            }

            await Task.Yield();
        }
    }

    private static ServerBootstrap CreateDeterministicArenaBootstrap(ServerConfig config)
    {
        WorldState world = ArenaZoneFactory.CreateWorld(config.ToSimulationConfig());
        ZoneState zone = world.Zones.Single(z => z.Id.Value == ArenaZoneFactory.ArenaZoneId);

        Vec2Fix player1Spawn = ArenaZoneFactory.ResolvePlayerSpawnPoint(1);
        EntityState deterministicTarget = new(
            new EntityId(9000),
            new Vec2Fix(player1Spawn.X - Fix32.One, player1Spawn.Y),
            Vec2Fix.Zero,
            100,
            100,
            true,
            Fix32.One,
            1,
            1,
            0,
            EntityKind.Npc,
            NextWanderChangeTick: int.MaxValue,
            WanderX: 0,
            WanderY: 0);

        ImmutableArray<EntityState> entities = zone.Entities
            .Add(deterministicTarget)
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray();

        ZoneState updatedZone = zone.WithEntities(entities);
        WorldState updatedWorld = world with
        {
            Zones = world.Zones.Select(z => z.Id.Value == ArenaZoneFactory.ArenaZoneId ? updatedZone : z).ToImmutableArray(),
            EntityLocations = entities.Select(e => new EntityLocation(e.Id, new ZoneId(ArenaZoneFactory.ArenaZoneId))).ToImmutableArray()
        };

        ImmutableArray<BootstrapPlayerRecord> players = ImmutableArray.Create(
            new BootstrapPlayerRecord("client-smoke-pr98", 1, null, null));

        return new ServerBootstrap(updatedWorld, config.Seed, players);
    }
}

internal sealed class InstrumentedClientTransport : IClientTransport
{
    private readonly IClientTransport _inner;
    private readonly SmokeDiagnostics _diagnostics;

    public InstrumentedClientTransport(IClientTransport inner, SmokeDiagnostics diagnostics)
    {
        _inner = inner;
        _diagnostics = diagnostics;
    }

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        => _inner.ConnectAsync(host, port, cancellationToken);

    public void Send(byte[] payload)
    {
        if (ProtocolCodec.TryDecodeClient(payload, out IClientMessage? message, out _)
            && message is Game.Protocol.CastSkillCommand cast)
        {
            _diagnostics.RecordCastAttempt(cast);
        }

        _inner.Send(payload);
    }

    public bool TryRead(out byte[] payload)
    {
        if (!_inner.TryRead(out payload))
        {
            return false;
        }

        if (payload.Length < 4)
        {
            return true;
        }

        int frameLength = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4));
        if (frameLength <= 0 || payload.Length < 4 + frameLength)
        {
            return true;
        }

        ReadOnlySpan<byte> framedPayload = payload.AsSpan(4, frameLength);
        if (ProtocolCodec.TryDecodeServer(framedPayload, out IServerMessage? message, out _))
        {
            _diagnostics.RecordServerMessage(message!);
        }

        return true;
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

internal sealed class SmokeDiagnostics
{
    private readonly List<string> _eventSamples = new();
    private readonly List<int> _availableAbilityIds = new();
    private int? _selfEntityId;
    private int? _firstSelfSeenTick;
    private int? _firstCastAttemptTick;
    private int _firstCastTargetPosXRaw;
    private int _firstCastTargetPosYRaw;
    private const int MaxEventSamples = 24;

    public int CastAttempts { get; private set; }

    public int CastAccepted { get; private set; }

    public int SnapshotHitsSeen { get; private set; }

    public int LastTickWithAnyEvent { get; private set; }

    public int CastRejectedDecode { get; private set; }

    public int CastRejectedValidate { get; private set; }

    public int CastRejectedUnknown { get; private set; }

    public int? SelfEntityId => _selfEntityId;

    public int? FirstSelfSeenTick => _firstSelfSeenTick;

    public string CastRejectStage { get; private set; } = "None";

    public int CastRejectRawCode { get; private set; }

    public string CastRejectRawName { get; private set; } = "None";

    public string CastRejectDetail { get; private set; } = "none";

    public void RecordAvailableAbilities(ImmutableArray<SkillDefinition> skills)
    {
        _availableAbilityIds.Clear();
        _availableAbilityIds.AddRange(skills.OrderBy(s => s.Id.Value).Select(s => s.Id.Value));
    }

    public bool HasAbility(int abilityId) => _availableAbilityIds.Contains(abilityId);

    public void RecordCastAttempt(Game.Protocol.CastSkillCommand cast)
    {
        CastAttempts++;
        _firstCastAttemptTick ??= cast.Tick;
        if (_firstCastAttemptTick == cast.Tick)
        {
            _firstCastTargetPosXRaw = cast.TargetPosXRaw;
            _firstCastTargetPosYRaw = cast.TargetPosYRaw;
        }

        AppendSample($"cast-attempt tick={cast.Tick} ability={cast.SkillId} x={cast.TargetPosXRaw} y={cast.TargetPosYRaw}");
    }

    public void RecordServerCastDiagnostics(ServerCastDiagnosticsEvent evt)
    {
        AppendSample($"server-cast stage={evt.Stage} tick={evt.Tick} ability={evt.AbilityId} code={evt.RawReasonCode} name={evt.RawReasonName} detail={evt.Detail}");

        switch (evt.Stage)
        {
            case ServerCastDiagStage.DecodeReject:
                CastRejectedDecode++;
                SetRawReject(evt);
                break;
            case ServerCastDiagStage.ValidateReject:
                CastRejectedValidate++;
                SetRawReject(evt);
                break;
            case ServerCastDiagStage.ApplyAccepted:
                CastAccepted++;
                LastTickWithAnyEvent = Math.Max(LastTickWithAnyEvent, evt.Tick);
                break;
            case ServerCastDiagStage.HitEmitted:
                LastTickWithAnyEvent = Math.Max(LastTickWithAnyEvent, evt.Tick);
                break;
            case ServerCastDiagStage.SelfAssigned:
                _selfEntityId = evt.RawReasonCode;
                LastTickWithAnyEvent = Math.Max(LastTickWithAnyEvent, evt.Tick);
                break;
            default:
                CastRejectedUnknown++;
                SetRawReject(evt);
                break;
        }
    }

    public void RecordServerMessage(IServerMessage message)
    {
        switch (message)
        {
            case EnterZoneAck ack:
                _selfEntityId = ack.EntityId;
                AppendSample($"enter-ack zone={ack.ZoneId} self={ack.EntityId}");
                break;
            case SnapshotV2 snapshot:
                if (_selfEntityId.HasValue && _firstSelfSeenTick is null && snapshot.Entities.Any(entity => entity.EntityId == _selfEntityId.Value))
                {
                    _firstSelfSeenTick = snapshot.Tick;
                }

                if (snapshot.HitEvents.Length > 0)
                {
                    SnapshotHitsSeen += snapshot.HitEvents.Length;
                    LastTickWithAnyEvent = Math.Max(LastTickWithAnyEvent, snapshot.Tick);
                    foreach (HitEventV1 hit in snapshot.HitEvents
                                 .OrderBy(evt => evt.TickId)
                                 .ThenBy(evt => evt.SourceEntityId)
                                 .ThenBy(evt => evt.TargetEntityId)
                                 .ThenBy(evt => evt.AbilityId)
                                 .ThenBy(evt => evt.EventSeq))
                    {
                        AppendSample($"hit tick={hit.TickId} src={hit.SourceEntityId} dst={hit.TargetEntityId} ability={hit.AbilityId} seq={hit.EventSeq}");
                    }
                }

                foreach (ProjectileEventV1 evt in snapshot.ProjectileEvents
                             .OrderBy(e => e.TickId)
                             .ThenBy(e => e.ProjectileId)
                             .ThenBy(e => e.Kind)
                             .Take(4))
                {
                    if (evt.Kind == (byte)ProjectileEventKind.Spawn || evt.Kind == (byte)ProjectileEventKind.Hit)
                    {
                        AppendSample($"proj tick={evt.TickId} kind={evt.Kind} src={evt.SourceEntityId} dst={evt.TargetEntityId} ability={evt.AbilityId}");
                    }
                }

                break;
        }
    }

    public string ToDeterministicReport(ClientRunResult result)
    {
        string availableAbilities = _availableAbilityIds.Count == 0
            ? "(none)"
            : string.Join(',', _availableAbilityIds);

        bool selfSpawnedAtCast = _firstSelfSeenTick.HasValue && _firstCastAttemptTick.HasValue && _firstSelfSeenTick.Value <= _firstCastAttemptTick.Value;

        return string.Join('\n',
            $"diag.ticksProcessed={result.TicksProcessed}",
            $"diag.hitEventsSeen={result.HitEventsSeen}",
            $"diag.castAttempts={CastAttempts}",
            $"diag.castAccepted={CastAccepted}",
            $"diag.castRejected.decode={CastRejectedDecode}",
            $"diag.castRejected.validate={CastRejectedValidate}",
            $"diag.castRejected.unknown={CastRejectedUnknown}",
            $"diag.castRejectStage={CastRejectStage}",
            $"diag.castRejectRawCode={CastRejectRawCode}",
            $"diag.castRejectRawName={CastRejectRawName}",
            $"diag.castRejectDetail={CastRejectDetail}",
            $"diag.snapshotHitsSeen={SnapshotHitsSeen}",
            $"diag.lastTickWithAnyEvent={LastTickWithAnyEvent}",
            $"diag.abilitiesAvailable=[{availableAbilities}]",
            $"diag.selfEntityId={_selfEntityId?.ToString() ?? "none"}",
            $"diag.firstSelfSeenTick={_firstSelfSeenTick?.ToString() ?? "none"}",
            $"diag.firstCastAttemptTick={_firstCastAttemptTick?.ToString() ?? "none"}",
            $"diag.selfSpawnedAtCast={selfSpawnedAtCast}",
            $"diag.firstCastPointRaw=({_firstCastTargetPosXRaw},{_firstCastTargetPosYRaw})",
            $"diag.firstCastPointFloat=({ToFloatString(_firstCastTargetPosXRaw)},{ToFloatString(_firstCastTargetPosYRaw)})",
            "diag.samples=",
            _eventSamples.Count == 0 ? "(none)" : string.Join('\n', _eventSamples.OrderBy(static x => x, StringComparer.Ordinal)));
    }

    private void SetRawReject(ServerCastDiagnosticsEvent evt)
    {
        CastRejectStage = evt.Stage.ToString();
        CastRejectRawCode = evt.RawReasonCode;
        CastRejectRawName = evt.RawReasonName;
        CastRejectDetail = evt.Detail;
    }

    private static string ToFloatString(int raw)
    {
        return ((double)raw / 65536d).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void AppendSample(string value)
    {
        if (_eventSamples.Count >= MaxEventSamples)
        {
            return;
        }

        _eventSamples.Add(value);
    }
}
