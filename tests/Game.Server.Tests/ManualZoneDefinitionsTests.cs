using System.Text.Json.Nodes;
using Xunit;
using Game.Core;
using Game.Server;

namespace Game.Server.Tests;

public sealed class ManualZoneDefinitionsTests
{
    [Fact]
    public void LoadZoneDefinitions_StableHash()
    {
        string fixtureDir = ResolveFixtureDirectory();

        string dirA = CreateTempDirectory();
        string dirB = CreateTempDirectory();
        try
        {
            File.Copy(Path.Combine(fixtureDir, "zone_1.json"), Path.Combine(dirA, "a_zone.json"));
            File.Copy(Path.Combine(fixtureDir, "zone_2.json"), Path.Combine(dirA, "z_zone.json"));

            File.Copy(Path.Combine(fixtureDir, "zone_1.json"), Path.Combine(dirB, "z_zone.json"));
            File.Copy(Path.Combine(fixtureDir, "zone_2.json"), Path.Combine(dirB, "a_zone.json"));

            ZoneDefinitions defsA1 = ZoneDefinitionsLoader.LoadFromDirectory(dirA);
            ZoneDefinitions defsA2 = ZoneDefinitionsLoader.LoadFromDirectory(dirA);
            ZoneDefinitions defsB = ZoneDefinitionsLoader.LoadFromDirectory(dirB);

            string hashA1 = ZoneDefinitionCanonicalizer.CanonicalizeAndHash(defsA1);
            string hashA2 = ZoneDefinitionCanonicalizer.CanonicalizeAndHash(defsA2);
            string hashB = ZoneDefinitionCanonicalizer.CanonicalizeAndHash(defsB);

            Assert.Equal(hashA1, hashA2);
            Assert.Equal(hashA1, hashB);
        }
        finally
        {
            Directory.Delete(dirA, recursive: true);
            Directory.Delete(dirB, recursive: true);
        }
    }

    [Fact]
    public void SpawnFromManualDefs_DeterministicChecksum()
    {
        string fixtureDir = ResolveFixtureDirectory();

        ServerConfig config = ServerConfig.Default(9001) with
        {
            ZoneCount = 2,
            ZoneDefinitionsPath = fixtureDir,
            NpcCountPerZone = 0
        };

        string checksumA = RunTicksAndChecksum(config, 200);
        string checksumB = RunTicksAndChecksum(config, 200);

        Assert.Equal(checksumA, checksumB);
    }

    [Fact]
    public void SameSeed_SameZoneDefs_SameChecksum()
    {
        string zonesDir = ResolveContentZonesDirectory();

        ServerConfig config = ServerConfig.Default(4242) with
        {
            ZoneCount = 3,
            ZoneDefinitionsPath = zonesDir,
            NpcCountPerZone = 0
        };

        string checksumA = RunTicksAndChecksum(config, 250);
        string checksumB = RunTicksAndChecksum(config, 250);

        Assert.Equal(checksumA, checksumB);
    }

    [Fact]
    public void ZonesDoNotInterfere()
    {
        string zonesDir = ResolveContentZonesDirectory();
        ZoneDefinitions defs = ZoneDefinitionsLoader.LoadFromDirectory(zonesDir);

        ServerConfig config = ServerConfig.Default(7171) with
        {
            ZoneCount = 3,
            ZoneDefinitionsPath = zonesDir,
            NpcCountPerZone = 0
        };

        ServerHost host = new(config);
        WorldState world = host.CurrentWorld;

        Dictionary<int, int> expectedNpcCounts = defs.Zones.ToDictionary(
            z => z.ZoneId.Value,
            z => z.NpcSpawns.Sum(s => s.Count));

        foreach (ZoneState zone in world.Zones)
        {
            int npcCount = zone.Entities.Count(e => e.Kind == EntityKind.Npc);
            Assert.Equal(expectedNpcCounts[zone.Id.Value], npcCount);
        }

        int[] allEntityIds = world.Zones.SelectMany(z => z.Entities).Select(e => e.Id.Value).ToArray();
        Assert.Equal(allEntityIds.Length, allEntityIds.Distinct().Count());

        foreach (ZoneDefinition zoneDef in defs.Zones)
        {
            Assert.True(world.TryGetZone(zoneDef.ZoneId, out ZoneState zone));

            HashSet<(int X, int Y)> allowedPoints = zoneDef.NpcSpawns
                .SelectMany(s => s.SpawnPoints.Take(s.Count))
                .Select(p => (p.X.Raw, p.Y.Raw))
                .ToHashSet();

            foreach (EntityState npc in zone.Entities.Where(e => e.Kind == EntityKind.Npc))
            {
                Assert.Contains((npc.Pos.X.Raw, npc.Pos.Y.Raw), allowedPoints);
            }
        }
    }

    [Fact]
    public void SpawnsWithinBounds()
    {
        string zonesDir = ResolveContentZonesDirectory();
        ZoneDefinitions defs = ZoneDefinitionsLoader.LoadFromDirectory(zonesDir);

        foreach (ZoneDefinition zone in defs.Zones)
        {
            foreach (NpcSpawnDefinition spawn in zone.NpcSpawns)
            {
                for (int i = 0; i < spawn.Count; i++)
                {
                    Assert.True(zone.Bounds.Contains(spawn.SpawnPoints[i]));
                }
            }
        }

        ServerConfig config = ServerConfig.Default(3232) with
        {
            ZoneCount = 3,
            ZoneDefinitionsPath = zonesDir,
            NpcCountPerZone = 0
        };

        ServerHost host = new(config);
        foreach (ZoneState zone in host.CurrentWorld.Zones)
        {
            ZoneDefinition zoneDef = defs.Zones.Single(z => z.ZoneId.Value == zone.Id.Value);
            foreach (EntityState npc in zone.Entities.Where(e => e.Kind == EntityKind.Npc))
            {
                Assert.True(zoneDef.Bounds.Contains(npc.Pos));
            }
        }

        string badDir = CreateTempDirectory();
        try
        {
            foreach (string sourceFile in Directory.GetFiles(zonesDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                File.Copy(sourceFile, Path.Combine(badDir, Path.GetFileName(sourceFile)));
            }

            string safePath = Path.Combine(badDir, "safe.zone.json");
            JsonObject root = JsonNode.Parse(File.ReadAllText(safePath))!.AsObject();
            root["NpcSpawns"] = new JsonArray
            {
                new JsonObject
                {
                    ["NpcArchetypeId"] = "rat",
                    ["Count"] = 1,
                    ["Level"] = 1,
                    ["SpawnPoints"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["X"] = -10.0,
                            ["Y"] = -10.0
                        }
                    }
                }
            };
            File.WriteAllText(safePath, root.ToJsonString());

            ServerConfig badConfig = config with { ZoneDefinitionsPath = badDir };
            Assert.ThrowsAny<Exception>(() => new ServerHost(badConfig));
        }
        finally
        {
            Directory.Delete(badDir, recursive: true);
        }
    }

    [Fact]
    public void Restart_NoDuplicateEntities()
    {
        string fixtureDir = ResolveFixtureDirectory();
        string dbPath = Path.Combine(CreateTempDirectory(), "restart_manual_defs.sqlite");

        try
        {
            ServerConfig config = ServerConfig.Default(777) with
            {
                ZoneCount = 2,
                ZoneDefinitionsPath = fixtureDir,
                NpcCountPerZone = 0,
                SnapshotEveryTicks = 1
            };

            ServerHost host = new(config);
            host.AdvanceTicks(60);
            host.SaveToSqlite(dbPath);

            ServerHost reloaded = ServerHost.LoadFromSqlite(config, dbPath);
            reloaded.AdvanceTicks(60);

            WorldState world = reloaded.CurrentWorld;
            int expectedNpcCount = ZoneDefinitionsLoader
                .LoadFromDirectory(fixtureDir)
                .Zones
                .Sum(zone => zone.NpcSpawns.Sum(spawn => spawn.Count));

            int[] ids = world.Zones.SelectMany(z => z.Entities).Select(e => e.Id.Value).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());

            int npcCount = world.Zones.SelectMany(z => z.Entities).Count(e => e.Kind == EntityKind.Npc);
            Assert.Equal(expectedNpcCount, npcCount);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            string? parent = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    private static string RunTicksAndChecksum(ServerConfig config, int ticks)
    {
        ServerHost host = new(config);
        host.AdvanceTicks(ticks);
        return StateChecksum.Compute(host.CurrentWorld);
    }

    private static string ResolveFixtureDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "tests", "Fixtures", "ZoneDefinitions");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate tests/Fixtures/ZoneDefinitions directory.");
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

        throw new DirectoryNotFoundException("Could not locate Content/Zones directory.");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "soulwars-zone-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
