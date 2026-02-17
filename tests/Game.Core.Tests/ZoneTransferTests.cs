using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class ZoneTransferTests
{
    [Fact]
    public void ZoneTransfer_AppliedEndOfTick()
    {
        SimulationConfig config = CreateConfig(seed: 1001) with { ZoneCount = 2 };
        WorldState state = Simulation.CreateInitialState(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1)))));

        int visitedDuringZoneStep = 0;
        List<ZoneTransferEvent> queued = new();
        List<ZoneTransferEvent> applied = new();
        SimulationInstrumentation instrumentation = new()
        {
            CountEntitiesVisited = count => visitedDuringZoneStep += count,
            OnZoneTransferQueued = queued.Add,
            OnZoneTransferApplied = applied.Add
        };

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: 1, MoveY: 0),
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(1), new ZoneId(1), ToZoneId: new ZoneId(2)))), instrumentation);

        Assert.True(visitedDuringZoneStep > 0);
        Assert.Single(queued);
        Assert.Single(applied);
        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone1));
        Assert.True(state.TryGetZone(new ZoneId(2), out ZoneState zone2));
        Assert.DoesNotContain(zone1.Entities, e => e.Id.Value == 1);
        Assert.Contains(zone2.Entities, e => e.Id.Value == 1);

        // Indirect pending-transfer empty check: next tick with no commands has no applied transfer events.
        applied.Clear();
        _ = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty), instrumentation);
        Assert.Empty(applied);
    }

    [Fact]
    public void ZoneTransfer_DeterministicOrder()
    {
        SimulationConfig config = CreateConfig(seed: 2002) with { ZoneCount = 2 };

        (ImmutableArray<ZoneTransferEvent> applied, string checksum) runA = RunSameTickTransfers(config);
        (ImmutableArray<ZoneTransferEvent> applied, string checksum) runB = RunSameTickTransfers(config);

        Assert.Equal(runA.checksum, runB.checksum);
        Assert.Equal(runA.applied, runB.applied);

        ImmutableArray<ZoneTransferEvent> expectedOrder = runA.applied
            .OrderBy(e => e.FromZoneId)
            .ThenBy(e => e.ToZoneId)
            .ThenBy(e => e.EntityId.Value)
            .ToImmutableArray();
        Assert.Equal(expectedOrder, runA.applied);
    }

    [Fact]
    public void ZoneTransfer_NoDuplicatesPerTick_FailsFast()
    {
        SimulationConfig config = CreateConfig(seed: 3003) with { ZoneCount = 2 };
        WorldState state = Simulation.CreateInitialState(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1)))));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(1), new ZoneId(1), ToZoneId: new ZoneId(2)),
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(1), new ZoneId(1), ToZoneId: new ZoneId(2))))));

        Assert.Contains("Duplicate ZoneTransferEvent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayVerify_MultiZone_Transfer_Passes()
    {
        SimulationConfig config = CreateConfig(seed: 4004) with { ZoneCount = 2 };

        ImmutableArray<string> globalsA = RunChecksumsWithTransfer(config, ticks: 8);
        ImmutableArray<string> globalsB = RunChecksumsWithTransfer(config, ticks: 8);

        Assert.Equal(globalsA, globalsB);
    }

    private static (ImmutableArray<ZoneTransferEvent> applied, string checksum) RunSameTickTransfers(SimulationConfig config)
    {
        WorldState state = Simulation.CreateInitialState(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1)),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1)),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(3), new ZoneId(2)),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(4), new ZoneId(2)))));

        List<ZoneTransferEvent> applied = new();
        SimulationInstrumentation instrumentation = new()
        {
            OnZoneTransferApplied = applied.Add
        };

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(1), new ZoneId(1), ToZoneId: new ZoneId(2)),
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(2), new ZoneId(1), ToZoneId: new ZoneId(2)),
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(3), new ZoneId(2), ToZoneId: new ZoneId(1)),
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(4), new ZoneId(2), ToZoneId: new ZoneId(1)))), instrumentation);

        return (applied.ToImmutableArray(), StateChecksum.Compute(state));
    }

    private static ImmutableArray<string> RunChecksumsWithTransfer(SimulationConfig config, int ticks)
    {
        WorldState state = Simulation.CreateInitialState(config);
        ImmutableArray<string>.Builder checksums = ImmutableArray.CreateBuilder<string>(ticks);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1)))));
        checksums.Add(StateChecksum.ComputeGlobalChecksum(state));

        for (int tick = 1; tick < ticks; tick++)
        {
            ImmutableArray<WorldCommand> commands = tick == 2
                ? ImmutableArray.Create(new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(1), new ZoneId(1), ToZoneId: new ZoneId(2)))
                : ImmutableArray<WorldCommand>.Empty;

            state = Simulation.Step(config, state, new Inputs(commands));
            checksums.Add(StateChecksum.ComputeGlobalChecksum(state));
        }

        return checksums.MoveToImmutable();
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: 2,
        MapWidth: 64,
        MapHeight: 64,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
