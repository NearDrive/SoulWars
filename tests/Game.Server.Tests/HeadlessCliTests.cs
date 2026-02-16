using Game.App.Headless;

namespace Game.Server.Tests;

public sealed class HeadlessCliTests
{
    [Fact]
    public void Cli_Smoke_StartStop()
    {
        int exitCode = Program.Main([
            "--seed", "123",
            "--zone-count", "1",
            "--bot-count", "0",
            "--port", "7777",
            "--ticks", "50"
        ]);

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData("--seed", "notAnInt")]
    [InlineData("--zone-count", "0")]
    [InlineData("--port", "-1")]
    public void Cli_InvalidConfig_ReturnsError(string key, string value)
    {
        int exitCode = Program.Main([
            key, value,
            "--bot-count", "0",
            "--ticks", "10"
        ]);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void Cli_SameSeed_SameArgs_SameChecksum_TwoRuns()
    {
        ServerAppConfig config = new(
            Seed: 555,
            Port: 7777,
            SqlitePath: null,
            ZoneCount: 2,
            BotCount: 8);

        RunResult first = Program.RunOnce(config, ticks: 120);
        RunResult second = Program.RunOnce(config, ticks: 120);

        Assert.Equal(first.Checksum, second.Checksum);
    }
}
