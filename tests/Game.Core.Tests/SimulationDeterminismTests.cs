using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class SimulationDeterminismTests
{
    [Fact]
    public void Determinism_SameSeedSameInputs_WithCollisions_SameChecksum()
    {
        const int tickCount = 500;
        SimulationConfig config = CreateConfig(seed: 123);

        string checksumA = RunSimulation(config, tickCount, validateCollisionInvariant: false);
        string checksumB = RunSimulation(config, tickCount, validateCollisionInvariant: false);

        Assert.Equal(checksumA, checksumB);
    }

    [Fact]
    public void Collision_PlayerNeverInsideSolidTile()
    {
        const int tickCount = 500;
        SimulationConfig config = CreateConfig(seed: 123);

        _ = RunSimulation(config, tickCount, validateCollisionInvariant: true);
    }

    [Fact]
    public void Determinism_DifferentSeed_DifferentChecksum()
    {
        const int tickCount = 500;
        SimulationConfig configA = CreateConfig(seed: 123);
        SimulationConfig configB = CreateConfig(seed: 124);

        string checksumA = RunSimulation(configA, tickCount, validateCollisionInvariant: false);
        string checksumB = RunSimulation(configB, tickCount, validateCollisionInvariant: false);

        Assert.NotEqual(checksumA, checksumB);
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

    private static string RunSimulation(SimulationConfig config, int tickCount, bool validateCollisionInvariant)
    {
        SimRng inputRng = new(seed: 999);
        WorldState state = Simulation.CreateInitialState(config);

        for (int tick = 0; tick < tickCount; tick++)
        {
            PlayerInput input = new(
                EntityId: new EntityId(1),
                MoveX: (sbyte)inputRng.NextInt(-1, 2),
                MoveY: (sbyte)inputRng.NextInt(-1, 2));

            Inputs inputs = new(ImmutableArray.Create(input));
            state = Simulation.Step(config, state, inputs);

            if (validateCollisionInvariant)
            {
                EntityState player = state.Entities.Single(entity => entity.Id.Value == 1);
                Assert.False(Physics2D.OverlapsSolidTile(player.Pos, config.Radius, state.Map));
            }
        }

        return StateChecksum.Compute(state);
    }
}
