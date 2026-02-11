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
    public void Determinism_SameSeedSameCommands_SameChecksum()
    {
        SimulationConfig config = CreateConfig(seed: 123);

        string checksumA = RunSequence(config);
        string checksumB = RunSequence(config);

        Assert.Equal(checksumA, checksumB);
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
        MapWidth: 64,
        MapHeight: 64);
}
