using Game.Client.Headless;
using Game.Client.Headless.Runtime;
using Game.Client.Headless.Transport;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests.ClientSmoke;

public sealed class ClientServerGoldenSmokeTests
{
    private const int RunTicks = 3600;
    private const string GoldenHash = "TODO_SET_FROM_CI";

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

        if (GoldenHash == "TODO_SET_FROM_CI")
        {
            Assert.Fail($"GoldenHash placeholder detected. Set GoldenHash to the CI-produced TraceHash: {result.TraceHash}");
        }

        Assert.True(
            string.Equals(result.TraceHash, GoldenHash, StringComparison.Ordinal),
            $"TraceHash mismatch. Expected GoldenHash='{GoldenHash}', Actual TraceHash='{result.TraceHash}'.");
    }

    [Fact]
    [Trait("Category", "PR98")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public async Task ClientSmoke_CastPoint_IsAccepted_AndProducesHitEvent_InArenaFixture()
    {
        (ClientRunResult result, SmokeDiagnostics diagnostics) = await RunArenaGoldenSmokeAsync(runTicks: 240);

        Assert.True(result.HandshakeOk, diagnostics.ToDeterministicReport(result));
        Assert.True(diagnostics.CastAttempts > 0, diagnostics.ToDeterministicReport(result));
        Assert.True(diagnostics.CastAccepted > 0, diagnostics.ToDeterministicReport(result));
        Assert.True(result.HitEventsSeen > 0, diagnostics.ToDeterministicReport(result));
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

        ServerHost host = new(config);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        ClientOptions options = new("inproc", 0, 1, "basic", ArenaZoneFactory.ArenaZoneId, 1, "client-smoke-pr98", StopOnFirstHit: false);
        await using InMemoryClientTransport innerTransport = new(endpoint);
        SmokeDiagnostics diagnostics = new();
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
            && message is CastSkillCommand cast)
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

        if (ProtocolCodec.TryDecodeServer(payload, out IServerMessage? message, out _))
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
    private int? _selfEntityId;
    private const int MaxEventSamples = 24;

    public int CastAttempts { get; private set; }

    public int CastAccepted { get; private set; }

    public int SnapshotHitsSeen { get; private set; }

    public int LastTickWithAnyEvent { get; private set; }

    public int CastRejectedUnknown => Math.Max(0, CastAttempts - CastAccepted);

    public void RecordCastAttempt(CastSkillCommand cast)
    {
        CastAttempts++;
        AppendSample($"cast-attempt tick={cast.Tick} ability={cast.SkillId} x={cast.TargetPosXRaw} y={cast.TargetPosYRaw}");
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

                int acceptedInSnapshot = snapshot.ProjectileEvents
                    .Count(evt => evt.Kind == (byte)ProjectileEventKind.Spawn
                                  && _selfEntityId.HasValue
                                  && evt.SourceEntityId == _selfEntityId.Value);
                if (acceptedInSnapshot > 0)
                {
                    CastAccepted += acceptedInSnapshot;
                    LastTickWithAnyEvent = Math.Max(LastTickWithAnyEvent, snapshot.Tick);
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
        return string.Join('\n',
            $"diag.ticksProcessed={result.TicksProcessed}",
            $"diag.hitEventsSeen={result.HitEventsSeen}",
            $"diag.castAttempts={CastAttempts}",
            $"diag.castAccepted={CastAccepted}",
            $"diag.castRejected.unknown={CastRejectedUnknown}",
            $"diag.snapshotHitsSeen={SnapshotHitsSeen}",
            $"diag.lastTickWithAnyEvent={LastTickWithAnyEvent}",
            "diag.samples=",
            _eventSamples.Count == 0 ? "(none)" : string.Join('\n', _eventSamples.OrderBy(static x => x, StringComparer.Ordinal)));
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
