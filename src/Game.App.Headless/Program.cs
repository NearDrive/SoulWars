using Game.BotRunner;
using Game.Server;
using Microsoft.Extensions.Logging;

namespace Game.App.Headless;

public static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitVerifyFail = 3;
    private const int ExitFixtureNotFound = 4;
    private const int ExitStressInvariantFail = 5;
    private const int ExitSoakFail = 6;
    private const int ExitPerfBudgetFail = 7;

    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            PrintUsage();
            return 1;
        }

        return args[0] switch
        {
            "--verify-mvp1" => VerifyMvp1(),
            "--run-scenario" => RunScenario(),
            "--stress-mvp2" => RunStressMvp2(),
            "--soak" => RunSoak(),
            "--perf-budgets" => RunPerfBudgets(),
            _ => UnknownMode(args[0])
        };
    }

    private static int VerifyMvp1()
    {
        FixtureInput fixture;
        try
        {
            fixture = LoadReplayFixture();
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitFixtureNotFound;
        }

        ReplayExecutionResult replayResult;
        using (fixture.Stream)
        {
            replayResult = ReplayRunner.RunReplayWithExpected(fixture.Stream);
        }

        string actual = NormalizeChecksum(replayResult.Checksum);
        string expected = !string.IsNullOrWhiteSpace(replayResult.ExpectedChecksum)
            ? NormalizeChecksum(replayResult.ExpectedChecksum)
            : actual;
        bool pass = string.Equals(expected, actual, StringComparison.Ordinal);

        Console.WriteLine("MVP1 VERIFY");
        Console.WriteLine($"fixture={fixture.DisplayPath}");
        Console.WriteLine($"expected={expected}");
        Console.WriteLine($"actual={actual}");
        Console.WriteLine($"result={(pass ? "PASS" : "FAIL")}");

        return pass ? ExitSuccess : ExitVerifyFail;
    }

    private static int RunScenario()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });

        ScenarioConfig config = new(
            BotCount: 2,
            TickCount: 200,
            SnapshotEveryTicks: 1,
            ServerSeed: BaselineScenario.Config.ServerSeed,
            BaseBotSeed: BaselineScenario.Config.BaseBotSeed,
            ZoneId: BaselineScenario.Config.ZoneId);

        ScenarioResult result = ScenarioRunner.RunDetailed(config, loggerFactory);

        Console.WriteLine("MVP1 SCENARIO");
        Console.WriteLine($"bots={result.Bots} ticks={result.Ticks} zone={config.ZoneId}");
        Console.WriteLine($"checksum={NormalizeChecksum(result.Checksum)}");
        Console.WriteLine($"messagesIn={result.MessagesIn} messagesOut={result.MessagesOut}");
        Console.WriteLine($"playersConnectedMax={result.PlayersConnectedMax}");
        Console.WriteLine($"invariantFailures={result.InvariantFailures}");
        Console.WriteLine($"result={(result.InvariantFailures == 0 ? "PASS" : "FAIL")}");

        return result.InvariantFailures == 0 ? ExitSuccess : 2;
    }


    private static int RunStressMvp2()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });

        ScenarioConfig config = BaselineScenario.CreateStressPreset();
        ScenarioResult result = ScenarioRunner.RunDetailed(config, loggerFactory);

        Console.WriteLine("MVP2 STRESS");
        Console.WriteLine($"bots={result.Bots} ticks={result.Ticks} zones={config.ZoneCount}");
        Console.WriteLine($"checksum={NormalizeChecksum(result.Checksum)}");
        Console.WriteLine($"messagesIn={result.MessagesIn} messagesOut={result.MessagesOut}");
        Console.WriteLine($"tickAvgMs={result.TickAvgMs:F3} tickP95Ms={result.TickP95Ms:F3}");
        Console.WriteLine($"invariantFailures={result.InvariantFailures}");
        Console.WriteLine($"result={(result.InvariantFailures == 0 ? "PASS" : "FAIL")}");

        return result.InvariantFailures == 0 ? ExitSuccess : ExitStressInvariantFail;
    }

    private static int RunSoak()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });

        ScenarioConfig config = BaselineScenario.CreateSoakPreset();
        ScenarioResult result = ScenarioRunner.RunDetailed(config, loggerFactory);

        Console.WriteLine("MVP4 SOAK");
        Console.WriteLine($"bots={result.Bots} ticks={result.Ticks} zones={config.ZoneCount}");
        Console.WriteLine($"checksum={NormalizeChecksum(result.Checksum)}");
        Console.WriteLine($"messagesIn={result.MessagesIn} messagesOut={result.MessagesOut}");
        Console.WriteLine($"sessions={result.ActiveSessions} entities={result.WorldEntityCount}");
        Console.WriteLine($"tickP50Ms={result.TickAvgMs:F3} tickP95Ms={result.TickP95Ms:F3}");
        if (result.GuardSnapshot is { } guard)
        {
            Console.WriteLine($"guards=maxInbound={guard.MaxInboundQueueLen} maxOutbound={guard.MaxOutboundQueueLen} maxEntities={guard.MaxEntityCount} maxPendingWorld={guard.MaxPendingWorldCommands} maxPendingAttack={guard.MaxPendingAttackIntents} guardFailures={guard.Failures}");
        }

        Console.WriteLine($"invariantFailures={result.InvariantFailures}");
        Console.WriteLine($"result={(result.InvariantFailures == 0 ? "PASS" : "FAIL")}");

        return result.InvariantFailures == 0 ? ExitSuccess : ExitSoakFail;
    }

    private static int RunPerfBudgets()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });

        PerfBudgetConfig budget = PerfBudgetConfig.Default;
        ScenarioConfig config = new(
            ServerSeed: 2025,
            TickCount: 2000,
            SnapshotEveryTicks: 1,
            BotCount: 50,
            ZoneId: 1,
            BaseBotSeed: 7000,
            ZoneCount: 3,
            NpcCount: 0,
            VisionRadiusTiles: 12);

        ScenarioResult result = ScenarioRunner.RunDetailed(config, loggerFactory);
        if (result.PerfSnapshot is null)
        {
            Console.Error.WriteLine("Perf snapshot missing from scenario result.");
            return ExitPerfBudgetFail;
        }

        PerfSnapshot snapshot = result.PerfSnapshot.Value;
        BudgetResult budgetResult = PerfBudgetEvaluator.Evaluate(snapshot, budget);
        string reportJson = PerfReportWriter.ToJson(snapshot, budget, budgetResult);
        string reportPath = Path.Combine(Environment.CurrentDirectory, "PerfReport.json");
        File.WriteAllText(reportPath, reportJson);

        Console.WriteLine("MVP5 PERF BUDGETS");
        Console.WriteLine($"bots={config.BotCount} ticks={config.TickCount} zones={config.ZoneCount}");
        Console.WriteLine($"checksum={NormalizeChecksum(result.Checksum)}");
        Console.WriteLine($"maxAoiChecksPerTick={snapshot.MaxAoiDistanceChecksPerTick}");
        Console.WriteLine($"maxCollisionChecksPerTick={snapshot.MaxCollisionChecksPerTick}");
        Console.WriteLine($"maxSnapshotsEncodedEntitiesPerTick={snapshot.MaxSnapshotsEncodedEntitiesPerTick}");
        Console.WriteLine($"maxOutboundBytesPerTick={snapshot.MaxOutboundBytesPerTick}");
        Console.WriteLine($"maxInboundBytesPerTick={snapshot.MaxInboundBytesPerTick}");
        Console.WriteLine($"maxCommandsProcessedPerTick={snapshot.MaxCommandsProcessedPerTick}");
        Console.WriteLine($"maxOutboundMessagesPerTick={snapshot.MaxOutboundMessagesPerTick}");
        Console.WriteLine($"maxInboundMessagesPerTick={snapshot.MaxInboundMessagesPerTick}");
        Console.WriteLine($"report={NormalizeDisplayPath(reportPath)}");

        if (!budgetResult.Ok)
        {
            foreach (string violation in budgetResult.Violations)
            {
                Console.WriteLine($"violation={violation}");
            }
        }

        Console.WriteLine($"result={(budgetResult.Ok ? "PASS" : "FAIL")}");
        return budgetResult.Ok ? ExitSuccess : ExitPerfBudgetFail;
    }


    private static int UnknownMode(string mode)
    {
        Console.Error.WriteLine($"Unknown mode: {mode}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: Game.App.Headless --verify-mvp1 | --run-scenario | --stress-mvp2 | --soak | --perf-budgets");
    }

    private static FixtureInput LoadReplayFixture()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string fixturesPath = Path.Combine(current.FullName, "tests", "Fixtures");
            if (Directory.Exists(fixturesPath))
            {
                string binaryCandidate = Path.Combine(fixturesPath, "replay_baseline.bin");
                if (File.Exists(binaryCandidate))
                {
                    return new FixtureInput(
                        DisplayPath: NormalizeDisplayPath(binaryCandidate),
                        Stream: File.OpenRead(binaryCandidate));
                }

                string hexCandidate = Path.Combine(fixturesPath, "replay_baseline.hex");
                if (File.Exists(hexCandidate))
                {
                    byte[] bytes = Convert.FromHexString(File.ReadAllText(hexCandidate).Trim());
                    return new FixtureInput(
                        DisplayPath: NormalizeDisplayPath(hexCandidate),
                        Stream: new MemoryStream(bytes, writable: false));
                }

                throw new DirectoryNotFoundException($"Found {NormalizeDisplayPath(fixturesPath)}, but replay_baseline.bin was not present.");
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate tests/Fixtures from AppContext.BaseDirectory.");
    }

    private static string NormalizeDisplayPath(string path)
    {
        return Path.GetRelativePath(Environment.CurrentDirectory, path).Replace('\\', '/');
    }

    private static string NormalizeChecksum(string checksum)
    {
        string normalized = checksum.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return normalized.ToLowerInvariant();
    }
}

internal sealed record FixtureInput(string DisplayPath, Stream Stream);
