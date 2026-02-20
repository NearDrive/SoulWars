using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class ReplayVerifyInstanceRestartTests
{
    [Fact]
    [Trait("Category", "PR65")]
    [Trait("Category", "ReplayVerify")]
    public void ReplayVerify_InstanceRestart_HasNoFirstDivergentTick()
    {
        const int totalTicks = 120;
        const int splitTick = 60;

        SimulationConfig config = CreateConfig(seed: 6503);
        ImmutableArray<TickReport> baseline = BuildTickReports(config, totalTicks, restartTick: null);
        ImmutableArray<TickReport> resumed = BuildTickReports(config, totalTicks, splitTick);

        Assert.Equal(baseline.Length, resumed.Length);

        int? firstDivergentTick = null;
        for (int i = 0; i < baseline.Length; i++)
        {
            if (!Equals(baseline[i], resumed[i]))
            {
                firstDivergentTick = baseline[i].Tick;
                break;
            }
        }

        Assert.Null(firstDivergentTick);
    }

    private static ImmutableArray<TickReport> BuildTickReports(SimulationConfig config, int totalTicks, int? restartTick)
    {
        WorldState state = Simulation.CreateInitialState(config);
        PartyRegistry parties = state.PartyRegistryOrEmpty.CreateParty(new EntityId(300));
        PartyId partyId = Assert.Single(parties.Parties).Id;
        (InstanceRegistry registry, _) = state.InstanceRegistryOrEmpty.CreateInstance(config.Seed, partyId, new ZoneId(1), state.Tick);
        state = state with { PartyRegistry = parties, InstanceRegistry = registry };

        ImmutableArray<TickReport>.Builder reports = ImmutableArray.CreateBuilder<TickReport>(totalTicks);

        for (int tick = 0; tick < totalTicks; tick++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
            string worldChecksum = StateChecksum.Compute(state);
            reports.Add(TickReportBuilder.Build(state, worldChecksum));

            if (restartTick.HasValue && tick + 1 == restartTick.Value)
            {
                byte[] snapshot = Game.Persistence.WorldStateSerializer.SaveToBytes(state);
                state = Game.Persistence.WorldStateSerializer.LoadFromBytes(snapshot);
            }
        }

        return reports.MoveToImmutable();
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
        NpcWanderPeriodTicks: 10,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
