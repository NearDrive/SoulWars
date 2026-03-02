using System.Globalization;

namespace Game.Client.Headless;

public sealed record ClientOptions(
    string Host,
    int Port,
    int ProtocolVersion,
    string Script,
    int ZoneId,
    int AbilityId,
    string AccountId)
{
    public static ClientOptions Parse(string[] args)
    {
        string host = "127.0.0.1";
        int port = 7000;
        int protocol = 1;
        string script = "basic";
        int zoneId = 1;
        int abilityId = 1;
        string accountId = "headless-client";

        for (int i = 0; i < args.Length; i++)
        {
            string current = args[i];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string? value = i + 1 < args.Length ? args[i + 1] : null;
            switch (current)
            {
                case "--host":
                    host = value ?? host;
                    i++;
                    break;
                case "--port":
                    port = int.Parse(value ?? "7000", CultureInfo.InvariantCulture);
                    i++;
                    break;
                case "--protocol":
                    protocol = int.Parse(value ?? "1", CultureInfo.InvariantCulture);
                    i++;
                    break;
                case "--script":
                    script = value ?? script;
                    i++;
                    break;
                case "--zone":
                    zoneId = int.Parse(value ?? "1", CultureInfo.InvariantCulture);
                    i++;
                    break;
                case "--ability":
                    abilityId = int.Parse(value ?? "1", CultureInfo.InvariantCulture);
                    i++;
                    break;
                case "--account":
                    accountId = value ?? accountId;
                    i++;
                    break;
            }
        }

        return new ClientOptions(host, port, protocol, script, zoneId, abilityId, accountId);
    }
}
