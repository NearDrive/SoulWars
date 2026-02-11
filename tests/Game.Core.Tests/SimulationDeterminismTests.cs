using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class SimulationDeterminismTests
{
    [Fact]
    public void Determinism_SameSeedSameInputs_SameChecksum()
    {
        const int tickCount = 200;
        SimulationConfig config = new(Seed: 123, TickHz: 20, Dt: 0.05f);

        string checksumA = RunSimulation(config, tickCount);
        string checksumB = RunSimulation(config, tickCount);

        Assert.Equal(checksumA, checksumB);
    }

    [Fact]
    public void Determinism_DifferentSeed_DifferentChecksum()
    {
        const int tickCount = 200;
        SimulationConfig configA = new(Seed: 123, TickHz: 20, Dt: 0.05f);
        SimulationConfig configB = new(Seed: 124, TickHz: 20, Dt: 0.05f);

        string checksumA = RunSimulation(configA, tickCount);
        string checksumB = RunSimulation(configB, tickCount);

        Assert.NotEqual(checksumA, checksumB);
    }

    private static string RunSimulation(SimulationConfig config, int tickCount)
    {
        SimRng inputRng = new(seed: 999);
        WorldState state = Simulation.CreateInitialState(config);

        for (int tick = 0; tick < tickCount; tick++)
        {
            PlayerInput input = new(
                EntityId: new EntityId(1),
                Dx: inputRng.NextInt(-1, 2),
                Dy: inputRng.NextInt(-1, 2));

            Inputs inputs = new(ImmutableArray.Create(input));
            state = Simulation.Step(config, state, inputs);
        }

        return StateChecksum.Compute(state);
    }
}
