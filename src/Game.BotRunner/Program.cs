namespace Game.BotRunner;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "record-baseline", StringComparison.OrdinalIgnoreCase))
        {
            string fixturePath = ResolveFixtureOutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);

            using FileStream output = File.Create(fixturePath);
            ScenarioResult result = ScenarioRunner.RunAndRecord(BaselineScenario.Config, output);

            Console.WriteLine($"baseline replay written: {fixturePath}");
            Console.WriteLine($"checksum: {result.Checksum}");
            return 0;
        }

        Console.WriteLine("Usage: dotnet run --project src/Game.BotRunner -- record-baseline");
        return 1;
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
