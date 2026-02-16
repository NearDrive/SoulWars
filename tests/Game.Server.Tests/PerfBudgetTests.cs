using Game.BotRunner;
using Game.Core;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class PerfBudgetTests
{
    [Fact]
    [Trait("Category", "Perf")]
    public void PerfBudgets_ReferenceScenario_WithinLimits()
    {
        ScenarioConfig config = new(
            ServerSeed: 2025,
            TickCount: 1000,
            SnapshotEveryTicks: 1,
            BotCount: 50,
            ZoneId: 1,
            BaseBotSeed: 7000,
            ZoneCount: 3,
            NpcCount: 0,
            VisionRadiusTiles: 12);

        ScenarioResult result = ScenarioRunner.RunDetailed(config);
        PerfSnapshot snapshot = Assert.IsType<PerfSnapshot>(result.PerfSnapshot);
        BudgetResult budgetResult = PerfBudgetEvaluator.Evaluate(snapshot, PerfBudgetConfig.Default);

        Assert.True(budgetResult.Ok, string.Join(" | ", budgetResult.Violations));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void PerfInstrumentation_DoesNotChangeChecksum()
    {
        ServerConfig serverConfig = ServerConfig.Default(777) with
        {
            SnapshotEveryTicks = 1,
            ZoneCount = 1,
            NpcCountPerZone = 2
        };

        string withoutPerf = RunServerAndChecksum(serverConfig, enablePerfSampling: false);
        string withPerf = RunServerAndChecksum(serverConfig, enablePerfSampling: true);

        Assert.Equal(withoutPerf, withPerf);
    }

    private static string RunServerAndChecksum(ServerConfig cfg, bool enablePerfSampling)
    {
        ServerHost host = new(cfg);
        InMemoryEndpoint endpoint = new();
        host.Connect(endpoint);

        BotClient client = new(0, 1, endpoint);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));

        int steps = 0;
        while (!client.HandshakeDone)
        {
            if (steps++ > 2000)
            {
                throw new InvalidOperationException("handshake failed");
            }

            cts.Token.ThrowIfCancellationRequested();
            client.PumpMessages(_ => { });
            client.SendHelloIfNeeded();
            if (client.HasWelcome)
            {
                client.EnterZone();
            }

            host.ProcessInboundOnce();
            client.PumpMessages(_ => { });
        }

        ScenarioChecksumBuilder checksum = new();
        BotAgent agent = new(new BotConfig(0, 9999, 1));

        for (int tick = 1; tick <= 120; tick++)
        {
            BotDecision decision = agent.Decide(client);
            int commandTick = Math.Max(tick, client.LastSnapshot?.Tick ?? tick);
            client.SendInput(commandTick, decision.MoveX, decision.MoveY);

            host.StepOnce();

            client.PumpMessages(message =>
            {
                if (message is Game.Protocol.Snapshot snapshot)
                {
                    checksum.AppendSnapshot(tick, 0, snapshot);
                }
            });

            if (enablePerfSampling && tick % 40 == 0)
            {
                _ = host.SnapshotAndResetPerfWindow();
            }
        }

        if (enablePerfSampling)
        {
            _ = host.SnapshotAndResetPerfWindow();
        }

        return checksum.BuildHexLower();
    }
}
