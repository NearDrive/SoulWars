using System.Security.Cryptography;
using System.Text;
using Game.BotRunner;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class Mvp7DodVerificationTests
{
    [Fact]
    [Trait("Category", "MVP7")]
    public void Mvp7Dod_Zones_ManualLoader_Works()
    {
        ManualZoneDefinitionsTests manualZones = new();
        manualZones.ZonesDoNotInterfere();
        manualZones.SpawnsWithinBounds();

        ScenarioConfig cfg = BaselineScenario.CreateStressPreset() with { ZoneCount = 3, TickCount = 800 };
        ScenarioResult r1 = ScenarioRunner.RunDetailed(cfg);
        ScenarioResult r2 = ScenarioRunner.RunDetailed(cfg);
        Assert.Equal(TestChecksum.NormalizeFullHex(r1.Checksum), TestChecksum.NormalizeFullHex(r2.Checksum));
    }

    [Fact]
    [Trait("Category", "MVP7")]
    public void Mvp7Dod_RiskGradient_3Zones_CountsMatch()
    {
        string zonesDir = ResolveContentZonesDirectory();
        ZoneDefinitions defs = ZoneDefinitionsLoader.LoadFromDirectory(zonesDir);

        ServerConfig config = ServerConfig.Default(4040) with
        {
            ZoneCount = 3,
            ZoneDefinitionsPath = zonesDir,
            NpcCountPerZone = 0
        };

        string checksumA = RunTicksAndChecksum(config, 300);
        string checksumB = RunTicksAndChecksum(config, 300);
        Assert.Equal(checksumA, checksumB);

        ServerHost host = new(config);
        WorldState world = host.CurrentWorld;
        Dictionary<int, int> expectedNpcCounts = defs.Zones.ToDictionary(z => z.ZoneId.Value, z => z.NpcSpawns.Sum(s => s.Count));
        foreach (ZoneState zone in world.Zones)
        {
            int npcCount = zone.Entities.Count(e => e.Kind == EntityKind.Npc);
            Assert.Equal(expectedNpcCounts[zone.Id.Value], npcCount);
        }

        string checksumWithCounts = ComputeCountsChecksum(world);
        Assert.False(string.IsNullOrWhiteSpace(checksumWithCounts));
    }

    [Fact]
    [Trait("Category", "MVP7")]
    [Trait("Category", "ReplayVerify")]
    public void Mvp7Dod_ReplayVerify_GoldenChecksum()
    {
        string goldenPrefix = File.ReadAllText(ResolveGoldenPath("mvp7_checksum.txt")).Trim().ToLowerInvariant();
        Assert.False(string.IsNullOrWhiteSpace(goldenPrefix));

        using Stream replay = OpenFixtureStream();
        ReplayExecutionResult result = ReplayRunner.RunReplayWithExpected(replay);
        string actual = TestChecksum.NormalizeFullHex(result.Checksum);

        Assert.StartsWith(goldenPrefix, actual, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Soak")]
    public void Mvp7Dod_Soak_NoCrash_InvariantsOk()
    {
        ScenarioConfig cfg = BaselineScenario.CreateSoakPreset() with { TickCount = 5000 };
        ScenarioResult result = ScenarioRunner.RunDetailed(cfg);
        Assert.Equal(0, result.InvariantFailures);
        Assert.NotNull(result.GuardSnapshot);
        Assert.Equal(0, result.GuardSnapshot!.Failures);
    }

    private static string RunTicksAndChecksum(ServerConfig config, int ticks)
    {
        ServerHost host = new(config);
        host.AdvanceTicks(ticks);
        return StateChecksum.Compute(host.CurrentWorld);
    }

    private static string ComputeCountsChecksum(WorldState world)
    {
        string payload = string.Join("|", world.Zones.OrderBy(z => z.Id.Value)
            .Select(z => $"{z.Id.Value}:{z.Entities.Count(e => e.Kind == EntityKind.Npc)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string ResolveContentZonesDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "Content", "Zones");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Content/Zones directory from AppContext.BaseDirectory.");
    }

    private static string ResolveGoldenPath(string fileName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "tests", "Game.Server.Tests", "Golden", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Unable to locate golden file '{fileName}'.");
    }

    private static Stream OpenFixtureStream()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string fixtures = Path.Combine(current.FullName, "tests", "Fixtures");
            string binaryCandidate = Path.Combine(fixtures, "replay_baseline.bin");
            if (File.Exists(binaryCandidate))
            {
                return File.OpenRead(binaryCandidate);
            }

            string hexCandidate = Path.Combine(fixtures, "replay_baseline.hex");
            if (File.Exists(hexCandidate))
            {
                string hex = File.ReadAllText(hexCandidate).Trim();
                byte[] bytes = Convert.FromHexString(hex);
                return new MemoryStream(bytes, writable: false);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Unable to locate tests/Fixtures/replay_baseline.bin or replay_baseline.hex from AppContext.BaseDirectory.");
    }
}
