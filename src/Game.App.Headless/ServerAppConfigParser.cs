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
            BotCount: 0,
            ZoneDefinitionsPath: null);
        error = string.Empty;

        if (args is null)
        {
            error = "Arguments array cannot be null.";
            return false;
        }

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (i + 1 >= args.Length)
                {
                    error = $"Argument '{key}' requires a value.";
                    return false;
                }

                string value = args[++i];
                switch (key)
                {
                    case "--seed":
                        config = config with { Seed = ParseInt(value, "--seed") };
                        break;
                    case "--port":
                        config = config with { Port = ParseInt(value, "--port") };
                        break;
                    case "--ports":
                        config = config with { Port = ParseInt(value, "--ports") };
                        break;
                    case "--sqlite":
                        config = config with { SqlitePath = string.IsNullOrWhiteSpace(value) ? null : value };
                        break;
                    case "--zone-count":
                        config = config with { ZoneCount = ParseInt(value, "--zone-count") };
                        break;
                    case "--zones":
                        config = config with { ZoneCount = ParseInt(value, "--zones") };
                        break;
                    case "--bot-count":
                        config = config with { BotCount = ParseInt(value, "--bot-count") };
                        break;
                    case "--zone-definitions":
                        config = config with { ZoneDefinitionsPath = string.IsNullOrWhiteSpace(value) ? null : value };
                        break;
                    default:
                        error = $"Unknown argument '{key}'.";
                        return false;
                }
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
