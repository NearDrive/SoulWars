using Microsoft.Extensions.Logging;

namespace Game.BotRunner;

public static class Program
{
    public static int Main(string[] args)
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

        if (args.Length == 1 && string.Equals(args[0], "record-baseline", StringComparison.OrdinalIgnoreCase))
        {
            string fixturePath = ResolveFixtureOutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);

            using FileStream output = File.Create(fixturePath);
            ScenarioResult result = new ScenarioRunner(loggerFactory).RunAndRecordDetailed(BaselineScenario.Config, output);

            Console.WriteLine($"baseline replay written: {fixturePath}");
            PrintSummary(BaselineScenario.Config, result);
            return result.InvariantFailures == 0 ? 0 : 2;
        }

        ScenarioConfig cfg = BaselineScenario.Config;
        ScenarioResult runResult = new ScenarioRunner(loggerFactory).RunDetailed(cfg);
        PrintSummary(cfg, runResult);
        return runResult.InvariantFailures == 0 ? 0 : 2;
    }

    private static void PrintSummary(ScenarioConfig cfg, ScenarioResult result)
    {
        Console.WriteLine("=== BOTRUNNER SUMMARY ===");
        Console.WriteLine($"bots={result.Bots} ticks={result.Ticks} seed={cfg.ServerSeed} zone={cfg.ZoneId}");
        Console.WriteLine($"checksum={result.Checksum}");
        Console.WriteLine($"playersConnectedMax={result.PlayersConnectedMax}");
        Console.WriteLine($"messagesIn={result.MessagesIn} messagesOut={result.MessagesOut}");
        Console.WriteLine($"tickAvgMs={result.TickAvgMs:F3} tickP95Ms={result.TickP95Ms:F3}");
        Console.WriteLine($"invariantFailures={result.InvariantFailures}");
        foreach (BotStats stat in result.BotStats.OrderBy(s => s.BotIndex))
        {
            Console.WriteLine($"bot[{stat.BotIndex}] snapshots={stat.SnapshotsReceived} errors={stat.Errors}");
        }

        Console.WriteLine("========================");
    }

    private static string ResolveFixtureOutputPath()
    {
        string root = FindRepositoryRoot(AppContext.BaseDirectory);
        return Path.Combine(root, BaselineScenario.FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Game.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root from AppContext.BaseDirectory.");
    }
}
