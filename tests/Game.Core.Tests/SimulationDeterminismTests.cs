using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class SimulationDeterminismTests
{
    [Fact]
    public void EnterZone_CreatesEntityInZone()
    {
        SimulationConfig config = CreateConfig(seed: 42);
        WorldState state = Simulation.CreateInitialState(config);

        Inputs commands = new(ImmutableArray.Create(
            new WorldCommand(
                Kind: WorldCommandKind.EnterZone,
                EntityId: new EntityId(1),
                ZoneId: new ZoneId(1))));

        state = Simulation.Step(config, state, commands);

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone));
        Assert.Single(zone.Entities);
        Assert.Equal(1, zone.Entities[0].Id.Value);
    }

    [Fact]
    public void LeaveZone_RemovesEntityFromZone()
    {
        SimulationConfig config = CreateConfig(seed: 42);
        WorldState state = Simulation.CreateInitialState(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1)))));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.LeaveZone, new EntityId(1), new ZoneId(1)))));

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone));
        Assert.DoesNotContain(zone.Entities, entity => entity.Id.Value == 1);
    }


    [Fact]
    public void Teleport_MovesEntityBetweenZones_NoDupes()
    {
        SimulationConfig config = CreateConfig(seed: 42) with { ZoneCount = 2 };
        WorldState state = Simulation.CreateInitialState(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1)))));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(1), new ZoneId(1), ToZoneId: new ZoneId(2)))));

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zoneA));
        Assert.True(state.TryGetZone(new ZoneId(2), out ZoneState zoneB));
        Assert.DoesNotContain(zoneA.Entities, e => e.Id.Value == 1);
        Assert.Contains(zoneB.Entities, e => e.Id.Value == 1);
        Assert.True(state.TryGetEntityZone(new EntityId(1), out ZoneId located));
        Assert.Equal(2, located.Value);
    }


    [Fact]
    public void Teleport_ChainedInSameTick_AppliesInSubmissionOrder()
    {
        SimulationConfig config = CreateConfig(seed: 2024) with { ZoneCount = 3 };
        WorldState state = Simulation.CreateInitialState(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(2)))));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(1), new ZoneId(2), ToZoneId: new ZoneId(1)),
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(1), new ZoneId(1), ToZoneId: new ZoneId(3)))));

        Assert.True(state.TryGetEntityZone(new EntityId(1), out ZoneId zoneId));
        Assert.Equal(3, zoneId.Value);
    }

    [Fact]
    public void Determinism_TeleportSequence_SameChecksum()
    {
        SimulationConfig config = CreateConfig(seed: 123) with { ZoneCount = 2 };

        string checksumA = RunTeleportSequence(config);
        string checksumB = RunTeleportSequence(config);

        Assert.Equal(checksumA, checksumB);
    }

    [Fact]
    public void Determinism_SameSeedSameCommands_SameChecksum()
    {
        SimulationConfig config = CreateConfig(seed: 123);

        string checksumA = RunSequence(config);
        string checksumB = RunSequence(config);

        Assert.Equal(checksumA, checksumB);
    }


    private static string RunTeleportSequence(SimulationConfig config)
    {
        WorldState state = Simulation.CreateInitialState(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1)))));

        for (int i = 0; i < 5; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
                new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: 1, MoveY: 0))));
        }

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(1), new ZoneId(1), ToZoneId: new ZoneId(2)))));

        for (int i = 0; i < 5; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
                new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(2), MoveX: 0, MoveY: 1))));
        }

        return StateChecksum.Compute(state);
    }

    private static string RunSequence(SimulationConfig config)
    {
        WorldState state = Simulation.CreateInitialState(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1)))));

        WorldCommand[] moveCommands =
        [
            new(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: 1, MoveY: 0),
            new(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: 1, MoveY: 1),
            new(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: 0, MoveY: 1),
            new(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: -1, MoveY: 1),
            new(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: -1, MoveY: 0)
        ];

        foreach (WorldCommand move in moveCommands)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(move)));
        }

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.LeaveZone, new EntityId(1), new ZoneId(1)))));

        return StateChecksum.Compute(state);
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277), // ~= 0.05
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384), // 0.25
        ZoneCount: 2,
        MapWidth: 64,
        MapHeight: 64,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6));
}
