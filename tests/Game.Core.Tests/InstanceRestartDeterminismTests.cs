using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR65")]
public sealed class InstanceRestartDeterminismTests
{
    [Fact]
    public void RestartAtTick100_MatchesNoRestartChecksumAtTick200()
    {
        const int totalTicks = 200;
        const int splitTick = 100;

        SimulationConfig config = CreateConfig(seed: 6502);
        WorldState baseline = RunWithInstance(config, totalTicks, restartTick: null);
        WorldState resumed = RunWithInstance(config, totalTicks, splitTick);

        Assert.Equal(StateChecksum.ComputeGlobalChecksum(baseline), StateChecksum.ComputeGlobalChecksum(resumed));
    }

    private static WorldState RunWithInstance(SimulationConfig config, int totalTicks, int? restartTick)
    {
        WorldState state = Simulation.CreateInitialState(config);
        PartyRegistry parties = state.PartyRegistryOrEmpty.CreateParty(new EntityId(200));
        PartyId partyId = Assert.Single(parties.Parties).Id;
        (InstanceRegistry registry, _) = state.InstanceRegistryOrEmpty.CreateInstance(config.Seed, partyId, new ZoneId(1), state.Tick);
        state = state with { PartyRegistry = parties, InstanceRegistry = registry };

        for (int tick = 0; tick < totalTicks; tick++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

            if (restartTick.HasValue && tick + 1 == restartTick.Value)
            {
                byte[] snapshot = WorldStateSerializer.SaveToBytes(state);
                state = WorldStateSerializer.LoadFromBytes(snapshot);
            }
        }

        return state;
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: 1,
        MapWidth: 16,
        MapHeight: 16,
        NpcCountPerZone: 1,
        NpcWanderPeriodTicks: 8,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
