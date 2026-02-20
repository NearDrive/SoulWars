using System.Collections.Immutable;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

public sealed class WorldStateMigrationTests
{
    public static IEnumerable<object[]> LegacyFixtures()
    {
        yield return ["world_v1.hex", "world_v1.checksum.txt"];
        yield return ["world_v2.hex", "world_v2.checksum.txt"];
        yield return ["world_v3.hex", "world_v3.checksum.txt"];
    }

    [Theory]
    [MemberData(nameof(LegacyFixtures))]
    public void Load_OldVersions_Migrates_And_SameChecksum(string fixtureFile, string checksumFile)
    {
        byte[] bytes = ReadHexFixture(Path.Combine(ResolveSnapshotFixtureDir(), fixtureFile));
        WorldState loaded = WorldStateSerializer.LoadFromBytes(bytes);

        byte[] reSerialized = WorldStateSerializer.SaveToBytes(loaded);
        int serializerVersion = BitConverter.ToInt32(reSerialized, 8);
        string worldChecksum = StateChecksum.Compute(loaded);
        string expectedChecksum = File.ReadAllText(Path.Combine(ResolveSnapshotFixtureDir(), checksumFile)).Trim();

        Assert.Equal(WorldStateSerializer.SerializerVersion, serializerVersion);
        Assert.Equal(expectedChecksum, worldChecksum);
    }

    [Theory]
    [MemberData(nameof(LegacyFixtures))]
    public void Restart_FromMigratedSnapshot_NoIdDrift(string fixtureFile, string _)
    {
        const int ticksToRun = 20;
        SimulationConfig config = new(
            Seed: 777,
            TickHz: 20,
            DtFix: new(3277),
            MoveSpeed: Fix32.FromInt(4),
            MaxSpeed: Fix32.FromInt(4),
            Radius: new(16384),
            ZoneCount: 1,
            MapWidth: 1,
            MapHeight: 1,
            NpcCountPerZone: 0,
            NpcWanderPeriodTicks: 10,
            NpcAggroRange: Fix32.FromInt(6),
            Invariants: InvariantOptions.Enabled);

        byte[] bytes = ReadHexFixture(Path.Combine(ResolveSnapshotFixtureDir(), fixtureFile));
        WorldState migrated = WorldStateSerializer.LoadFromBytes(bytes);

        WorldState continued = migrated;
        List<string> continuedChecksums = new();
        for (int tick = 0; tick < ticksToRun; tick++)
        {
            continued = Simulation.Step(config, continued, new Inputs(ImmutableArray<WorldCommand>.Empty));
            AssertUniqueEntityIds(continued);
            continuedChecksums.Add(StateChecksum.Compute(continued));
        }

        byte[] migratedV4 = WorldStateSerializer.SaveToBytes(migrated);
        WorldState restarted = WorldStateSerializer.LoadFromBytes(migratedV4);

        for (int tick = 0; tick < ticksToRun; tick++)
        {
            restarted = Simulation.Step(config, restarted, new Inputs(ImmutableArray<WorldCommand>.Empty));
            AssertUniqueEntityIds(restarted);

            string restartChecksum = StateChecksum.Compute(restarted);
            Assert.Equal(continuedChecksums[tick], restartChecksum);
        }
    }


    private static byte[] ReadHexFixture(string path)
    {
        string hex = File.ReadAllText(path).Trim();
        return Convert.FromHexString(hex);
    }

    private static void AssertUniqueEntityIds(WorldState world)
    {
        foreach (ZoneState zone in world.Zones)
        {
            int[] ids = zone.EntitiesData.AliveIds.Select(id => id.Value).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.True(ids.SequenceEqual(ids.OrderBy(v => v)), "Entity ids must remain sorted.");
        }
    }

    private static string ResolveSnapshotFixtureDir()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "tests", "Fixtures", "SnapshotMigration");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate tests/Fixtures/SnapshotMigration from AppContext.BaseDirectory.");
    }
}
