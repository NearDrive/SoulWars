using System.Collections.Immutable;
using System.Text.Json;
using Game.Core;

namespace Game.Server;

public static class ZoneDefinitionsLoader
{
    public static ZoneDefinitions LoadFromDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException("Zone definitions path cannot be empty.");
        }

        if (!Directory.Exists(directoryPath))
        {
            throw new InvalidOperationException($"Zone definitions directory does not exist: '{directoryPath}'.");
        }

        string[] files = Directory
            .GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException($"Zone definitions directory '{directoryPath}' does not contain any .json files.");
        }

        HashSet<int> seenZoneIds = new();
        ImmutableArray<ZoneDefinition>.Builder zones = ImmutableArray.CreateBuilder<ZoneDefinition>(files.Length);

        foreach (string file in files)
        {
            ZoneDefinition zone = ParseAndValidateZone(file);
            if (!seenZoneIds.Add(zone.ZoneId.Value))
            {
                throw new InvalidOperationException($"Duplicate ZoneId '{zone.ZoneId.Value}' found in '{file}'.");
            }

            zones.Add(zone);
        }

        return new ZoneDefinitions(zones
            .ToImmutable()
            .OrderBy(z => z.ZoneId.Value)
            .ToImmutableArray());
    }

    private static ZoneDefinition ParseAndValidateZone(string path)
    {
        string json = File.ReadAllText(path);
        ZoneDefinitionFile? file = JsonSerializer.Deserialize<ZoneDefinitionFile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (file is null)
        {
            throw new InvalidOperationException($"Invalid zone json in '{path}'.");
        }

        if (file.ZoneId <= 0)
        {
            throw new InvalidOperationException($"ZoneId must be > 0 in '{path}'.");
        }

        if (file.StaticObstacles is null)
        {
            throw new InvalidOperationException($"Missing required field 'StaticObstacles' in '{path}'.");
        }

        if (file.NpcSpawns is null)
        {
            throw new InvalidOperationException($"Missing required field 'NpcSpawns' in '{path}'.");
        }

        ImmutableArray<ZoneAabb>.Builder obstacles = ImmutableArray.CreateBuilder<ZoneAabb>(file.StaticObstacles.Count);
        foreach (ZoneAabbFile obstacle in file.StaticObstacles)
        {
            ValidateFinite(obstacle.CenterX, nameof(obstacle.CenterX), path);
            ValidateFinite(obstacle.CenterY, nameof(obstacle.CenterY), path);
            ValidateFinite(obstacle.HalfExtentX, nameof(obstacle.HalfExtentX), path);
            ValidateFinite(obstacle.HalfExtentY, nameof(obstacle.HalfExtentY), path);

            if (obstacle.HalfExtentX <= 0 || obstacle.HalfExtentY <= 0)
            {
                throw new InvalidOperationException($"Invalid AABB in '{path}': half extents must be > 0.");
            }

            obstacles.Add(new ZoneAabb(
                Center: new Vec2Fix(Fix32.FromFloat(obstacle.CenterX), Fix32.FromFloat(obstacle.CenterY)),
                HalfExtents: new Vec2Fix(Fix32.FromFloat(obstacle.HalfExtentX), Fix32.FromFloat(obstacle.HalfExtentY))));
        }

        ImmutableArray<NpcSpawnDefinition>.Builder spawns = ImmutableArray.CreateBuilder<NpcSpawnDefinition>(file.NpcSpawns.Count);
        foreach (NpcSpawnFile spawn in file.NpcSpawns)
        {
            if (string.IsNullOrWhiteSpace(spawn.NpcArchetypeId))
            {
                throw new InvalidOperationException($"NpcArchetypeId is required in '{path}'.");
            }

            if (spawn.Count < 0)
            {
                throw new InvalidOperationException($"NpcSpawn.Count must be >= 0 in '{path}'.");
            }

            if (spawn.SpawnPoints is null)
            {
                throw new InvalidOperationException($"NpcSpawn.SpawnPoints is required in '{path}'.");
            }

            if (spawn.Count > spawn.SpawnPoints.Count)
            {
                throw new InvalidOperationException($"NpcSpawn.Count cannot exceed SpawnPoints.Length in '{path}' for archetype '{spawn.NpcArchetypeId}'.");
            }

            ImmutableArray<Vec2Fix>.Builder points = ImmutableArray.CreateBuilder<Vec2Fix>(spawn.SpawnPoints.Count);
            foreach (Vec2File point in spawn.SpawnPoints)
            {
                ValidateFinite(point.X, nameof(point.X), path);
                ValidateFinite(point.Y, nameof(point.Y), path);
                points.Add(new Vec2Fix(Fix32.FromFloat(point.X), Fix32.FromFloat(point.Y)));
            }

            spawns.Add(new NpcSpawnDefinition(
                NpcArchetypeId: spawn.NpcArchetypeId,
                Count: spawn.Count,
                Level: spawn.Level,
                SpawnPoints: points.ToImmutableArray()));
        }

        return new ZoneDefinition(
            ZoneId: new ZoneId(file.ZoneId),
            StaticObstacles: obstacles.ToImmutableArray(),
            NpcSpawns: spawns.ToImmutableArray(),
            LootRules: file.LootRules is null ? null : new LootRulesDefinition());
    }

    private static void ValidateFinite(float value, string field, string path)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new InvalidOperationException($"Field '{field}' in '{path}' must be finite.");
        }
    }

    private sealed class ZoneDefinitionFile
    {
        public int ZoneId { get; set; }
        public List<ZoneAabbFile>? StaticObstacles { get; set; }
        public List<NpcSpawnFile>? NpcSpawns { get; set; }
        public object? LootRules { get; set; }
    }

    private sealed class ZoneAabbFile
    {
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float HalfExtentX { get; set; }
        public float HalfExtentY { get; set; }
    }

    private sealed class NpcSpawnFile
    {
        public string NpcArchetypeId { get; set; } = string.Empty;
        public int Count { get; set; }
        public int Level { get; set; }
        public List<Vec2File>? SpawnPoints { get; set; }
    }

    private sealed class Vec2File
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}
