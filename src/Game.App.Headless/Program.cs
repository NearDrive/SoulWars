using Game.BotRunner;
using Game.Core;
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
        if (TryRunLegacyMode(args, out int legacyExitCode))
        {
            return legacyExitCode;
        }

        if (!TryParseTicks(args, out int ticks, out string[] configArgs, out string tickError))
        {
            Console.Error.WriteLine(tickError);
            PrintUsage();
            return 1;
        }

        if (!ServerAppConfigParser.TryParse(configArgs, out ServerAppConfig appConfig, out string configError))
        {
            Console.Error.WriteLine(configError);
            PrintUsage();
            return 1;
        }

        if (ticks <= 0)
        {
            Console.Error.WriteLine("--ticks must be greater than 0.");
            PrintUsage();
            return 1;
        }

        RunResult result = RunOnce(appConfig, ticks);
        Console.WriteLine($"checksum={NormalizeChecksum(result.Checksum)}");
        Console.WriteLine($"ticks={result.Ticks}");
        Console.WriteLine($"zones={appConfig.ZoneCount}");
        Console.WriteLine($"bots={appConfig.BotCount}");
        Console.WriteLine($"port={appConfig.Port}");

        return ExitSuccess;
    }

    public static RunResult RunOnce(ServerAppConfig appConfig, int ticks)
    {
        ServerConfig serverConfig = CreateRuntimeConfig(appConfig);

        if (appConfig.BotCount > 0)
        {
            ScenarioConfig scenarioConfig = new(
                ServerSeed: appConfig.Seed,
                TickCount: ticks,
                SnapshotEveryTicks: 1,
                BotCount: appConfig.BotCount,
                ZoneId: 1,
                BaseBotSeed: unchecked(appConfig.Seed + 5000),
                ZoneCount: appConfig.ZoneCount,
                NpcCount: 0,
                VisionRadiusTiles: 12);

            ScenarioResult result = ScenarioRunner.RunDetailed(scenarioConfig);
            return new RunResult(result.Checksum, result.Ticks);
        }

        ServerHost host = new(serverConfig);
        host.AdvanceTicks(ticks);

        if (!string.IsNullOrWhiteSpace(appConfig.SqlitePath))
        {
            host.SaveToSqlite(appConfig.SqlitePath);
        }

        return new RunResult(StateChecksum.Compute(host.CurrentWorld), ticks);
    }

    private static bool TryRunLegacyMode(string[] args, out int exitCode)
    {
        exitCode = ExitSuccess;
        if (args.Length != 1)
        {
            return false;
        }

        exitCode = args[0] switch
        {
            "--verify-mvp1" => VerifyMvp1(),
            "--run-scenario" => RunScenario(),
            "--stress-mvp2" => RunStressMvp2(),
            "--soak" => RunSoak(),
            "--perf-budgets" => RunPerfBudgets(),
            _ => int.MinValue
        };

        return exitCode != int.MinValue;
    }

    private static bool TryParseTicks(string[] args, out int ticks, out string[] configArgs, out string error)
    {
        ticks = 50;
        error = string.Empty;
        List<string> filtered = new(args.Length);

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!string.Equals(arg, "--ticks", StringComparison.Ordinal))
            {
                filtered.Add(arg);
                continue;
            }

            if (i + 1 >= args.Length)
            {
                configArgs = Array.Empty<string>();
                error = "Argument '--ticks' requires a value.";
                return false;
            }

            if (!int.TryParse(args[++i], out ticks))
            {
                configArgs = Array.Empty<string>();
                error = $"Argument '--ticks' expects an integer value. Received '{args[i]}'.";
                return false;
            }
        }

        configArgs = filtered.ToArray();
        return true;
    }

    private static ServerConfig CreateRuntimeConfig(ServerAppConfig appConfig)
    {
        return ServerConfig.Default(appConfig.Seed) with
        {
            Seed = appConfig.Seed,
            ZoneCount = appConfig.ZoneCount
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

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Game.App.Headless --seed <int> --port <int> --sqlite <path> --zone-count <int> --bot-count <int> [--ticks <int>]");
        Console.WriteLine("  (alias: --ports for --port)");
        Console.WriteLine("Legacy modes:");
        Console.WriteLine("  Game.App.Headless --verify-mvp1 | --run-scenario | --stress-mvp2 | --soak | --perf-budgets");
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

public sealed record RunResult(string Checksum, int Ticks);

internal sealed record FixtureInput(string DisplayPath, Stream Stream);
