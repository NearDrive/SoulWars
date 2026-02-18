using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class HierarchicalChecksumTests
{
    [Fact]
    public void PerZoneChecksum_Stable()
    {
        ImmutableArray<ZoneChecksum> runA = RunTicksAndGetZoneChecksums(ticks: 20);
        ImmutableArray<ZoneChecksum> runB = RunTicksAndGetZoneChecksums(ticks: 20);

        Assert.Equal([1, 2], runA.Select(z => z.ZoneId).ToArray());
        Assert.Equal([1, 2], runB.Select(z => z.ZoneId).ToArray());

        Assert.Equal(runA[0].Value, runB[0].Value);
        Assert.Equal(runA[1].Value, runB[1].Value);
    }

    [Fact]
    public void GlobalChecksum_StableAcrossRuns()
    {
        string globalA = RunTicksAndGetGlobalChecksum(ticks: 25);
        string globalB = RunTicksAndGetGlobalChecksum(ticks: 25);

        Assert.Equal(globalA, globalB);
    }

    [Fact]
    public void GlobalChecksum_EqualsHashOfOrderedZoneChecksums()
    {
        SimulationConfig config = CreateConfig(seed: 9876);
        WorldState state = Simulation.CreateInitialState(config);

        for (int i = 0; i < 10; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        ImmutableArray<ZoneChecksum> orderedZones = StateChecksum.ComputeZoneChecksums(state);
        string expectedGlobal = StateChecksum.ComputeGlobalChecksum(state);

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(orderedZones.Length);
        foreach (ZoneChecksum zoneChecksum in orderedZones)
        {
            writer.Write(zoneChecksum.ZoneId);
            writer.Write(zoneChecksum.Value);
        }

        writer.Flush();
        string recomputed = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray())).ToLowerInvariant();

        Assert.Equal(expectedGlobal, recomputed);
    }

    private static ImmutableArray<ZoneChecksum> RunTicksAndGetZoneChecksums(int ticks)
    {
        SimulationConfig config = CreateConfig(seed: 4321);
        WorldState state = Simulation.CreateInitialState(config);

        for (int i = 0; i < ticks; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        return StateChecksum.ComputeZoneChecksums(state);
    }

    private static string RunTicksAndGetGlobalChecksum(int ticks)
    {
        SimulationConfig config = CreateConfig(seed: 4321);
        WorldState state = Simulation.CreateInitialState(config);

        for (int i = 0; i < ticks; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        return StateChecksum.ComputeGlobalChecksum(state);
    }

    private static SimulationConfig CreateConfig(int seed) => SimulationConfig.Default(seed) with
    {
        ZoneCount = 2,
        NpcCountPerZone = 0
    };
}
