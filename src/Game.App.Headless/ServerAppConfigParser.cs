namespace Game.App.Headless;

public static class ServerAppConfigParser
{
    public static bool TryParse(string[] args, out ServerAppConfig config, out string error)
    {
        config = new ServerAppConfig(
            Seed: 12345,
            Port: 7777,
            SqlitePath: null,
            ZoneCount: 1,
            BotCount: 0);
        error = string.Empty;

        if (args is null)
        {
            error = "Arguments array cannot be null.";
            return false;
        }

        Dictionary<string, Action<string>> handlers = new(StringComparer.Ordinal)
        {
            ["--seed"] = value => config = config with { Seed = ParseInt(value, "--seed") },
            ["--port"] = value => config = config with { Port = ParseInt(value, "--port") },
            ["--ports"] = value => config = config with { Port = ParseInt(value, "--ports") },
            ["--sqlite"] = value => config = config with { SqlitePath = string.IsNullOrWhiteSpace(value) ? null : value },
            ["--zone-count"] = value => config = config with { ZoneCount = ParseInt(value, "--zone-count") },
            ["--zones"] = value => config = config with { ZoneCount = ParseInt(value, "--zones") },
            ["--bot-count"] = value => config = config with { BotCount = ParseInt(value, "--bot-count") }
        };

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (!handlers.TryGetValue(key, out Action<string>? handler))
                {
                    error = $"Unknown argument '{key}'.";
                    return false;
                }

                if (i + 1 >= args.Length)
                {
                    error = $"Argument '{key}' requires a value.";
                    return false;
                }

                handler(args[++i]);
            }
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }

        if (config.Port <= 0 || config.Port > 65535)
        {
            error = "--port/--ports must be between 1 and 65535.";
            return false;
        }

        if (config.ZoneCount <= 0)
        {
            error = "--zone-count must be greater than 0.";
            return false;
        }

        if (config.BotCount < 0)
        {
            error = "--bot-count must be greater than or equal to 0.";
            return false;
        }

        return true;
    }

    private static int ParseInt(string value, string name)
    {
        if (!int.TryParse(value, out int parsed))
        {
            throw new FormatException($"Argument '{name}' expects an integer value. Received '{value}'.");
        }

        return parsed;
    }
}
