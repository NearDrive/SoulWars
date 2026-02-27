using System.Collections;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "PR87")]
public sealed class InputValidation_MoveBoundsTests
{
    [Fact]
    [Trait("Category", "PR87")]
    [Trait("Category", "Canary")]
    public void Move_ThatWouldLeaveZoneBounds_IsRejected_ByServerValidation()
    {
        ServerHost host = new(ServerConfig.Default(seed: 8701) with { MapWidth = 4, MapHeight = 4, NpcCountPerZone = 0 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "move-bounds")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.StepOnce();

        Snapshot snapshot = DrainLatestSnapshot(endpoint);
        SnapshotEntity self = snapshot.Entities.Single();
        int prevX = self.PosXRaw;
        int prevY = self.PosYRaw;

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(snapshot.Tick + 1, -1, -1)));
        host.StepOnce();

        Snapshot next = DrainLatestSnapshot(endpoint);
        SnapshotEntity nextSelf = next.Entities.Single();

        Assert.Equal(prevX, nextSelf.PosXRaw);
        Assert.Equal(prevY, nextSelf.PosYRaw);
    }

    private static Snapshot DrainLatestSnapshot(InMemoryEndpoint endpoint)
    {
        Snapshot? latest = null;
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.DecodeServer(payload) is Snapshot snapshot)
            {
                latest = snapshot;
            }
        }

        return latest ?? throw new Xunit.Sdk.XunitException("Expected at least one snapshot.");
    }
}

[Trait("Category", "PR87")]
public sealed class InputValidation_CastPointBoundsAndCooldownTests
{
    [Fact]
    [Trait("Category", "PR87")]
    [Trait("Category", "Canary")]
    public void CastPoint_OutOfBounds_IsRejected()
    {
        ServerHost host = new(ServerConfig.Default(seed: 8702) with { NpcCountPerZone = 0 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "cast-bounds")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.StepOnce();
        DrainAll(endpoint);

        CastSkillCommand cast = new(
            Tick: host.CurrentWorld.Tick + 1,
            CasterId: 0,
            SkillId: 101,
            ZoneId: 1,
            TargetKind: (byte)CastTargetKind.Point,
            TargetEntityId: 0,
            TargetPosXRaw: -1,
            TargetPosYRaw: 0);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(cast));
        host.ProcessInboundOnce();

        Assert.Equal(0, host.PendingCastSkillCommandCount);
    }

    [Fact]
    [Trait("Category", "PR87")]
    [Trait("Category", "Canary")]
    public void CastPoint_InBounds_ButCooldownActive_IsRejected_AndWithoutCooldown_IsAccepted()
    {
        ServerHost host = new(ServerConfig.Default(seed: 8703) with { NpcCountPerZone = 0 });
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "cast-cooldown")));
        endpoint.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.StepOnce();

        Snapshot snapshot = DrainLatestSnapshot(endpoint);
        SnapshotEntity self = snapshot.Entities.Single();

        CastSkillCommand first = new(
            Tick: snapshot.Tick + 1,
            CasterId: 0,
            SkillId: 202,
            ZoneId: 1,
            TargetKind: (byte)CastTargetKind.Point,
            TargetEntityId: 0,
            TargetPosXRaw: self.PosXRaw,
            TargetPosYRaw: self.PosYRaw);

        endpoint.EnqueueToServer(ProtocolCodec.Encode(first));
        host.ProcessInboundOnce();
        Assert.Equal(1, host.PendingCastSkillCommandCount);

        CastSkillCommand secondSameTick = first with { Tick = snapshot.Tick + 1 };
        endpoint.EnqueueToServer(ProtocolCodec.Encode(secondSameTick));
        host.ProcessInboundOnce();
        Assert.Equal(1, host.PendingCastSkillCommandCount);

        host.AdvanceSimulationOnce();

        CastSkillCommand thirdNextTick = first with { Tick = host.CurrentWorld.Tick + 1 };
        endpoint.EnqueueToServer(ProtocolCodec.Encode(thirdNextTick));
        host.ProcessInboundOnce();
        Assert.Equal(1, host.PendingCastSkillCommandCount);
    }

    private static Snapshot DrainLatestSnapshot(InMemoryEndpoint endpoint)
    {
        Snapshot? latest = null;
        while (endpoint.TryDequeueFromServer(out byte[] payload))
        {
            if (ProtocolCodec.DecodeServer(payload) is Snapshot snapshot)
            {
                latest = snapshot;
            }
        }

        return latest ?? throw new Xunit.Sdk.XunitException("Expected at least one snapshot.");
    }

    private static void DrainAll(InMemoryEndpoint endpoint)
    {
        while (endpoint.TryDequeueFromServer(out _))
        {
        }
    }
}

[Trait("Category", "PR87")]
public sealed class InputOrdering_DeterministicQueueTests
{
    [Fact]
    [Trait("Category", "PR87")]
    [Trait("Category", "Canary")]
    public void PendingInputs_AreProcessedIn_TickSessionSequence_Order()
    {
        ServerHost host = new(ServerConfig.Default(seed: 8704) with { NpcCountPerZone = 0 });
        InMemoryEndpoint endpointA = new();
        InMemoryEndpoint endpointB = new();

        SessionId sidA = host.Connect(endpointA);
        SessionId sidB = host.Connect(endpointB);

        endpointA.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "queue-a")));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new HandshakeRequest(ProtocolConstants.CurrentProtocolVersion, "queue-b")));
        endpointA.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new EnterZoneRequest(1)));
        host.StepOnce();

        DrainAll(endpointA);
        DrainAll(endpointB);

        int targetTick = host.CurrentWorld.Tick + 1;
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(targetTick, 1, 0)));
        endpointA.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(targetTick, 1, 0)));
        endpointA.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(targetTick, 0, 1)));
        endpointB.EnqueueToServer(ProtocolCodec.Encode(new InputCommand(targetTick, 0, 1)));

        host.ProcessInboundOnce();

        IEnumerable ordered = ReadOrderedPendingInputs(host, targetTick);
        List<(int Tick, int SessionId, int Sequence)> observed = ordered
            .Cast<object>()
            .Select(item => (
                Tick: (int)item.GetType().GetProperty("Tick")!.GetValue(item)!,
                SessionId: (int)item.GetType().GetProperty("SessionId")!.GetValue(item)!,
                Sequence: (int)item.GetType().GetProperty("Sequence")!.GetValue(item)!))
            .ToList();

        List<(int Tick, int SessionId, int Sequence)> expected = new()
        {
            (targetTick, Math.Min(sidA.Value, sidB.Value), 1),
            (targetTick, Math.Min(sidA.Value, sidB.Value), 2),
            (targetTick, Math.Max(sidA.Value, sidB.Value), 1),
            (targetTick, Math.Max(sidA.Value, sidB.Value), 2)
        };

        Assert.Equal(expected, observed);
    }

    private static IEnumerable ReadOrderedPendingInputs(ServerHost host, int targetTick)
    {
        System.Reflection.MethodInfo method = typeof(ServerHost)
            .GetMethod("OrderedPendingInputs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("Could not access OrderedPendingInputs.");

        object? result = method.Invoke(host, new object[] { targetTick });
        return result as IEnumerable ?? throw new Xunit.Sdk.XunitException("OrderedPendingInputs did not return IEnumerable.");
    }

    private static void DrainAll(InMemoryEndpoint endpoint)
    {
        while (endpoint.TryDequeueFromServer(out _))
        {
        }
    }
}
