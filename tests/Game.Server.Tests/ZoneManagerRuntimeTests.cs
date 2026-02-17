using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class ZoneManagerRuntimeTests
{
    [Fact]
    public void TwoZones_TickOrderDeterministic()
    {
        ServerConfig config = ServerConfig.Default(seed: 700) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 1,
            NpcCountPerZone = 0
        };

        ServerHost zoneTwo = new(config);
        ServerHost zoneOne = new(config);

        List<int> trace = new();
        ServerRuntime runtime = new();
        runtime.ConfigureZones(new[]
        {
            new KeyValuePair<int, ServerHost>(2, zoneTwo),
            new KeyValuePair<int, ServerHost>(1, zoneOne)
        });
        runtime.ZoneTickTraceSink = zoneId => trace.Add(zoneId);

        runtime.AdvanceTicks(4);

        Assert.Equal(new[] { 1, 2, 1, 2, 1, 2, 1, 2 }, trace);
        Assert.Equal(4, zoneOne.CurrentWorld.Tick);
        Assert.Equal(4, zoneTwo.CurrentWorld.Tick);
    }

    [Fact]
    public void TwoRuns_MultiZone_SameChecksum()
    {
        string first = RunTwoZoneScenarioAndChecksum();
        string second = RunTwoZoneScenarioAndChecksum();

        Assert.Equal(first, second);
    }

    private static string RunTwoZoneScenarioAndChecksum()
    {
        ServerConfig config = ServerConfig.Default(seed: 812) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 1,
            NpcCountPerZone = 0
        };

        ServerRuntime runtime = new();
        runtime.ConfigureZones(new[]
        {
            new KeyValuePair<int, ServerHost>(2, new ServerHost(config)),
            new KeyValuePair<int, ServerHost>(1, new ServerHost(config))
        });

        runtime.AdvanceTicks(64);
        return runtime.ComputeManagedWorldChecksum();
    }
}
