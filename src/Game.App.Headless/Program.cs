using Game.BotRunner;
using Microsoft.Extensions.Logging;

namespace Game.App.Headless;

public static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitVerifyFail = 3;
    private const int ExitFixtureNotFound = 4;

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
        bool hasExpectedChecksum = !string.IsNullOrWhiteSpace(replayResult.ExpectedChecksum);
        string expected = hasExpectedChecksum
            ? NormalizeChecksum(replayResult.ExpectedChecksum!)
            : "<missing>";
        bool pass = hasExpectedChecksum && string.Equals(expected, actual, StringComparison.Ordinal);

        Console.WriteLine("MVP1 VERIFY");
        Console.WriteLine($"fixture={fixture.DisplayPath}");
        Console.WriteLine($"expected={expected}");
        Console.WriteLine($"actual={actual}");
        if (!hasExpectedChecksum)
        {
            Console.WriteLine("error=fixture did not contain an expected checksum");
        }
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

    private static int UnknownMode(string mode)
    {
        Console.Error.WriteLine($"Unknown mode: {mode}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: Game.App.Headless --verify-mvp1 | --run-scenario");
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
