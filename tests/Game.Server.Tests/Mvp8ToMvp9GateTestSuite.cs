using System.Collections.Immutable;
using Game.BotRunner;
using Game.Core;
using Game.Persistence;
using Game.Persistence.Sqlite;
using Game.Server;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Game.Server.Tests;

public sealed class Mvp8ToMvp9GateTestSuite
{
    private static readonly string[] LegacySnapshotFixtures = ["world_v1", "world_v2", "world_v3"];

    [Fact]
    [Trait("Category", "Gate")]
    public void ReplayVerify_Passes_InCI()
    {
        using Stream replayStream = TestReplayRunnerHarness.OpenCanonicalReplayStream();
        ReplayExecutionResult result = ReplayRunner.RunReplayWithExpected(replayStream);

        string actualChecksum = TestChecksum.NormalizeFullHex(result.Checksum);
        Assert.False(string.IsNullOrWhiteSpace(actualChecksum));

        if (!string.IsNullOrWhiteSpace(result.ExpectedChecksum))
        {
            Assert.Equal(TestChecksum.NormalizeFullHex(result.ExpectedChecksum), actualChecksum);
        }
    }

    [Fact]
    [Trait("Category", "Gate")]
    public void ReplayVerify_Mismatch_ProducesDiagnosticBundle()
    {
        string outputDir = TestReplayRunnerHarness.ResetTempDir(nameof(ReplayVerify_Mismatch_ProducesDiagnosticBundle));

        using Stream replayStream = TestReplayRunnerHarness.CreateReplayWithWrongExpectedChecksum();
        ReplayVerificationException ex = Assert.Throws<ReplayVerificationException>(() =>
            ReplayRunner.RunReplayWithExpected(replayStream, verifyOptions: new ReplayVerifyOptions(outputDir)));

        Assert.True(ex.DivergentTick >= 0, $"Expected non-negative divergent tick, got {ex.DivergentTick}.");
        Assert.False(string.IsNullOrWhiteSpace(ex.ExpectedChecksum));
        Assert.False(string.IsNullOrWhiteSpace(ex.ActualChecksum));
        Assert.NotEqual(TestChecksum.NormalizeFullHex(ex.ExpectedChecksum), TestChecksum.NormalizeFullHex(ex.ActualChecksum));

        string[] requiredArtifacts =
        [
            "expected_checksum.txt",
            "actual_checksum.txt",
            "tickreport_expected.jsonl",
            "tickreport_actual.jsonl"
        ];

        foreach (string artifact in requiredArtifacts)
        {
            string fullPath = Path.Combine(outputDir, artifact);
            Assert.True(File.Exists(fullPath), $"Missing diagnostic artifact: {fullPath}");
            Assert.True(new FileInfo(fullPath).Length > 0, $"Artifact is empty: {fullPath}");
        }

        string summaryPath = Path.Combine(outputDir, "mismatch_summary.txt");
        if (File.Exists(summaryPath))
        {
            Assert.True(new FileInfo(summaryPath).Length > 0, $"Artifact is empty: {summaryPath}");
        }
    }

    [Fact]
    [Trait("Category", "Gate")]
    public void Snapshot_Load_ValidatesChecksum()
    {
        string dbPath = TestReplayRunnerHarness.CreateDeterministicTempFilePath(nameof(Snapshot_Load_ValidatesChecksum), ".db");
        TestReplayRunnerHarness.TryDeleteFile(dbPath);

        try
        {
            ServerConfig config = ServerConfig.Default(seed: 9341);
            ServerHost host = new(config);
            host.AdvanceTicks(16);
            host.SaveToSqlite(dbPath);

            string storedChecksum = ReadLatestStoredChecksum(dbPath);
            ServerHost loaded = ServerHost.LoadFromSqlite(config, dbPath);
            string actualChecksum = StateChecksum.Compute(loaded.CurrentWorld);

            Assert.Equal(storedChecksum, actualChecksum);
        }
        finally
        {
            TestReplayRunnerHarness.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "Gate")]
    public void Snapshot_Load_BadChecksum_FailsFast()
    {
        string dbPath = TestReplayRunnerHarness.CreateDeterministicTempFilePath(nameof(Snapshot_Load_BadChecksum_FailsFast), ".db");
        TestReplayRunnerHarness.TryDeleteFile(dbPath);

        try
        {
            ServerConfig config = ServerConfig.Default(seed: 7788);
            ServerHost host = new(config);
            host.AdvanceTicks(12);
            host.SaveToSqlite(dbPath);

            const string badChecksum = "0000000000000000000000000000000000000000000000000000000000000000";
            using (SqliteConnection connection = new($"Data Source={dbPath}"))
            {
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "UPDATE world_snapshots SET checksum = $checksum WHERE id = (SELECT id FROM world_snapshots ORDER BY saved_at_tick DESC, id DESC LIMIT 1);";
                command.Parameters.AddWithValue("$checksum", badChecksum);
                command.ExecuteNonQuery();
            }

            SnapshotChecksumMismatchException ex = Assert.Throws<SnapshotChecksumMismatchException>(() => ServerHost.LoadFromSqlite(config, dbPath));
            Assert.Contains("expected=", ex.Message, StringComparison.Ordinal);
            Assert.Contains("actual=", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TestReplayRunnerHarness.TryDeleteFile(dbPath);
        }
    }

    [Theory]
    [Trait("Category", "Gate")]
    [MemberData(nameof(GetLegacySnapshotFixtures))]
    public void Snapshot_OldVersions_MigrateToCurrentVersion(string fixtureKey)
    {
        string fixtureDir = TestReplayRunnerHarness.ResolveSnapshotFixtureDirectory();
        byte[] fixtureBytes = Convert.FromHexString(File.ReadAllText(Path.Combine(fixtureDir, $"{fixtureKey}.hex")).Trim());
        string expectedChecksum = File.ReadAllText(Path.Combine(fixtureDir, $"{fixtureKey}.checksum.txt")).Trim();

        WorldState loaded = WorldStateSerializer.LoadFromBytes(fixtureBytes);
        byte[] migratedBytes = WorldStateSerializer.SaveToBytes(loaded);

        int serializerVersion = BitConverter.ToInt32(migratedBytes, 8);
        string actualChecksum = StateChecksum.Compute(loaded);

        Assert.Equal(WorldStateSerializer.SerializerVersion, serializerVersion);
        Assert.Equal(expectedChecksum, actualChecksum);
    }

    public static IEnumerable<object[]> GetLegacySnapshotFixtures() => LegacySnapshotFixtures.Select(f => new object[] { f });

    [Fact]
    [Trait("Category", "Gate")]
    public void Snapshot_Restart_NoIdDrift_NoChecksumDrift()
    {
        const int ticksBeforeSnapshot = 20;
        const int ticksAfterSnapshot = 20;

        SimulationConfig config = TestReplayRunnerHarness.CreateSimulationConfig(seed: 2201);
        WorldState world = Simulation.CreateInitialState(config);
        world = Simulation.Step(config, world, new Inputs(ImmutableArray.Create(new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1)))));

        for (int tick = 1; tick <= ticksBeforeSnapshot; tick++)
        {
            world = Simulation.Step(config, world, new Inputs(ImmutableArray.Create(new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: DeterministicAxis(tick, 1), MoveY: DeterministicAxis(tick, 2)))));
            AssertUniqueEntityIds(world);
        }

        byte[] snapshot = WorldStateSerializer.SaveToBytes(world);

        WorldState continuePath = world;
        List<string> continueChecksums = new();
        for (int tick = ticksBeforeSnapshot + 1; tick <= ticksBeforeSnapshot + ticksAfterSnapshot; tick++)
        {
            continuePath = Simulation.Step(config, continuePath, new Inputs(ImmutableArray.Create(new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: DeterministicAxis(tick, 1), MoveY: DeterministicAxis(tick, 2)))));
            AssertUniqueEntityIds(continuePath);
            continueChecksums.Add(StateChecksum.Compute(continuePath));
        }

        WorldState restartPath = WorldStateSerializer.LoadFromBytes(snapshot);
        for (int i = 0; i < ticksAfterSnapshot; i++)
        {
            int tick = ticksBeforeSnapshot + i + 1;
            restartPath = Simulation.Step(config, restartPath, new Inputs(ImmutableArray.Create(new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: DeterministicAxis(tick, 1), MoveY: DeterministicAxis(tick, 2)))));
            AssertUniqueEntityIds(restartPath);
            Assert.Equal(continueChecksums[i], StateChecksum.Compute(restartPath));
        }
    }

    [Fact]
    [Trait("Category", "Gate")]
    public void TwoRuns_SameSeedSameReplay_SameFinalChecksum()
    {
        using Stream replayA = TestReplayRunnerHarness.OpenCanonicalReplayStream();
        using Stream replayB = TestReplayRunnerHarness.OpenCanonicalReplayStream();

        string checksumA = TestChecksum.NormalizeFullHex(ReplayRunner.RunReplayWithExpected(replayA).Checksum);
        string checksumB = TestChecksum.NormalizeFullHex(ReplayRunner.RunReplayWithExpected(replayB).Checksum);

        Assert.Equal(checksumA, checksumB);
    }

    [Fact]
    [Trait("Category", "Gate")]
    public void TwoRuns_SameSeedSameReplay_TickReport_ByteForByte()
    {
        string outputDirA = TestReplayRunnerHarness.ResetTempDir(nameof(TwoRuns_SameSeedSameReplay_TickReport_ByteForByte) + "-A");
        string outputDirB = TestReplayRunnerHarness.ResetTempDir(nameof(TwoRuns_SameSeedSameReplay_TickReport_ByteForByte) + "-B");

        byte[] reportA = RunReplayAndCaptureActualTickReport(outputDirA, out string checksumA);
        byte[] reportB = RunReplayAndCaptureActualTickReport(outputDirB, out string checksumB);

        Assert.Equal(checksumA, checksumB);
        Assert.Equal(reportA, reportB);
    }

    [Fact]
    [Trait("Category", "Gate")]
    public void NoTemporaryFeatureFlags()
    {
        string[] prohibitedPatterns = ["TEMP_", "TODO_REMOVE", "HACK_", "DEBUG_ONLY", "Temporary", "RemoveAfter"];
        string srcRoot = Path.Combine(TestReplayRunnerHarness.ResolveRepositoryRoot(), "src");

        List<string> violations = FindTextMatches(srcRoot, prohibitedPatterns, path => true);
        Assert.True(violations.Count == 0,
            "Temporary feature flags found in /src:\n" + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Category", "Gate")]
    public void NoTodoInCriticalPaths()
    {
        string repoRoot = TestReplayRunnerHarness.ResolveRepositoryRoot();
        string[] criticalRoots =
        [
            Path.Combine(repoRoot, "src", "Game.Server"),
            Path.Combine(repoRoot, "src", "Game.Core"),
            Path.Combine(repoRoot, "src", "Game.BotRunner"),
            Path.Combine(repoRoot, "src", "Game.Persistence"),
            Path.Combine(repoRoot, "src", "Game.Persistence.Sqlite")
        ];

        List<string> violations = new();
        foreach (string root in criticalRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            violations.AddRange(FindTextMatches(root, ["TODO", "FIXME"], path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)));
        }

        Assert.True(violations.Count == 0,
            "TODO/FIXME markers found in critical runtime paths:\n" + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Category", "Gate")]
    public void NoDebugLogsAffectSimOrdering()
    {
        string repoRoot = TestReplayRunnerHarness.ResolveRepositoryRoot();
        string[] criticalRoots =
        [
            Path.Combine(repoRoot, "src", "Game.Core"),
            Path.Combine(repoRoot, "src", "Game.Server"),
            Path.Combine(repoRoot, "src", "Game.Persistence"),
            Path.Combine(repoRoot, "src", "Game.Persistence.Sqlite"),
            Path.Combine(repoRoot, "src", "Game.BotRunner")
        ];

        static bool IsCriticalFile(string path)
        {
            string name = Path.GetFileName(path);
            return name.Contains("Sim", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Replay", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Snapshot", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("WorldState", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("ServerHost", StringComparison.OrdinalIgnoreCase);
        }

        List<string> violations = new();
        foreach (string root in criticalRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            violations.AddRange(FindTextMatches(root, ["Console.WriteLine", "Debug.WriteLine"], IsCriticalFile));
        }

        Assert.True(violations.Count == 0,
            "Debug logs found in sim/replay/snapshot critical paths:\n" + string.Join(Environment.NewLine, violations));
    }

    private static byte[] RunReplayAndCaptureActualTickReport(string outputDir, out string actualChecksum)
    {
        using Stream replayStream = TestReplayRunnerHarness.CreateReplayWithWrongExpectedChecksum();
        ReplayVerificationException ex = Assert.Throws<ReplayVerificationException>(() =>
            ReplayRunner.RunReplayWithExpected(replayStream, verifyOptions: new ReplayVerifyOptions(outputDir)));

        actualChecksum = TestChecksum.NormalizeFullHex(ex.ActualChecksum);
        string actualTickReportPath = Path.Combine(outputDir, "tickreport_actual.jsonl");
        Assert.True(File.Exists(actualTickReportPath), $"Missing tick report artifact: {actualTickReportPath}");
        return File.ReadAllBytes(actualTickReportPath);
    }

    private static List<string> FindTextMatches(string root, IReadOnlyList<string> patterns, Func<string, bool> includeFile)
    {
        List<string> violations = new();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || file.Contains(Path.DirectorySeparatorChar + "tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            if (!includeFile(file))
            {
                continue;
            }

            int lineNo = 0;
            foreach (string line in File.ReadLines(file))
            {
                lineNo++;
                foreach (string pattern in patterns)
                {
                    if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{Path.GetRelativePath(TestReplayRunnerHarness.ResolveRepositoryRoot(), file)}:{lineNo} => {pattern}");
                    }
                }
            }
        }

        return violations;
    }

    private static string ReadLatestStoredChecksum(string dbPath)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT checksum FROM world_snapshots ORDER BY saved_at_tick DESC, id DESC LIMIT 1;";
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static sbyte DeterministicAxis(int tick, int salt)
    {
        int value = ((tick * 31) + salt) % 3;
        return value switch
        {
            0 => (sbyte)-1,
            1 => (sbyte)0,
            _ => (sbyte)1
        };
    }

    private static void AssertUniqueEntityIds(WorldState world)
    {
        foreach (ZoneState zone in world.Zones)
        {
            int[] ids = zone.EntitiesData.AliveIds.Select(id => id.Value).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
        }
    }
}

internal static class TestReplayRunnerHarness
{
    public static string ResolveRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "Game.sln");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root containing Game.sln.");
    }

    public static string ResolveSnapshotFixtureDirectory()
    {
        string candidate = Path.Combine(ResolveRepositoryRoot(), "tests", "Fixtures", "SnapshotMigration");
        if (!Directory.Exists(candidate))
        {
            throw new DirectoryNotFoundException($"Unable to locate snapshot fixture directory: {candidate}");
        }

        return candidate;
    }

    public static Stream OpenCanonicalReplayStream()
    {
        string fixturePath = Path.Combine(ResolveRepositoryRoot(), "tests", "Fixtures", "replay_baseline.hex");
        string hex = File.ReadAllText(fixturePath).Trim();
        return new MemoryStream(Convert.FromHexString(hex), writable: false);
    }

    public static Stream CreateReplayWithWrongExpectedChecksum()
    {
        using Stream source = OpenCanonicalReplayStream();
        using ReplayReader reader = new(source);

        MemoryStream output = new();
        using ReplayWriter writer = new(output, reader.Header);

        const string wrongExpected = "0000000000000000000000000000000000000000000000000000000000000000";
        bool finalWritten = false;
        while (reader.TryReadNext(out ReplayEvent evt))
        {
            if (evt.RecordType == ReplayRecordType.TickInputs)
            {
                writer.WriteTickInputs(evt.Tick, evt.Moves.AsSpan());
                continue;
            }

            writer.WriteFinalChecksum(wrongExpected);
            finalWritten = true;
        }

        if (!finalWritten)
        {
            writer.WriteFinalChecksum(wrongExpected);
        }

        output.Position = 0;
        return output;
    }

    public static string ResetTempDir(string testName)
    {
        string path = Path.Combine(Path.GetTempPath(), "soulwars-gate-tests", testName);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
        return path;
    }

    public static string CreateDeterministicTempFilePath(string testName, string extension)
    {
        string dir = Path.Combine(Path.GetTempPath(), "soulwars-gate-tests", testName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "snapshot" + extension);
    }

    public static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static SimulationConfig CreateSimulationConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: 1,
        MapWidth: 16,
        MapHeight: 16,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 15,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
