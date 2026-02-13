using Game.BotRunner;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class ScenarioRunnerTests
{
    [Fact]
    public void ScenarioRunner_Baseline_IsDeterministic()
    {
        ScenarioConfig cfg = BaselineScenario.Config;

        string checksum1 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));
        string checksum2 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));

        Assert.Equal(checksum1, checksum2);
        Assert.StartsWith(BaselineChecksums.ScenarioBaselinePrefix, checksum1, StringComparison.Ordinal);
    }

    [Fact]
    public void ScenarioRunner_ResultContainsSummaryAndMetrics()
    {
        ScenarioConfig cfg = new(
            ServerSeed: 123,
            TickCount: 50,
            SnapshotEveryTicks: 1,
            BotCount: 2,
            ZoneId: 1,
            BaseBotSeed: 999);

        ScenarioResult result = new ScenarioRunner().RunDetailed(cfg);

        Assert.True(result.MessagesOut > 0);
        Assert.True(result.MessagesIn > 0);
        Assert.All(result.BotStats, stats => Assert.True(stats.SnapshotsReceived > 0));
        Assert.True(result.TickAvgMs > 0);
        Assert.True(result.TickP95Ms >= result.TickAvgMs);
        Assert.Equal(0, result.InvariantFailures);
    }

    [Fact]
    public void ScenarioRunner_MetricsSnapshotStable_Smoke()
    {
        ScenarioConfig cfg = new(
            ServerSeed: 124,
            TickCount: 20,
            SnapshotEveryTicks: 1,
            BotCount: 1,
            ZoneId: 1,
            BaseBotSeed: 99);

        ScenarioResult result = new ScenarioRunner().RunDetailed(cfg);

        Assert.True(result.PlayersConnectedMax >= 1);
        Assert.True(result.TickAvgMs > 0);
        Assert.True(result.MessagesIn > 0);
    }

    [Fact]
    public void ScenarioRunner_WithNpcs_AndTwoBots_IsDeterministic()
    {
        ScenarioConfig cfg = new(
            ServerSeed: 451,
            TickCount: 400,
            SnapshotEveryTicks: 1,
            BotCount: 2,
            ZoneId: 1,
            BaseBotSeed: 777,
            NpcCount: 4);

        string checksum1 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));
        string checksum2 = TestChecksum.NormalizeFullHex(ScenarioRunner.Run(cfg));

        Assert.Equal(checksum1, checksum2);
    }

    [Fact]
    public void Bots_AttackNpcs_AndSnapshotsStayMonotonic()
    {
        ServerConfig serverConfig = ServerConfig.Default(seed: 951) with
        {
            SnapshotEveryTicks = 1,
            NpcCountPerZone = 3
        };

        ServerHost host = new(serverConfig);
        List<BotClient> clients = new();
        List<BotAgent> agents = new();

        for (int i = 0; i < 2; i++)
        {
            InMemoryEndpoint endpoint = new();
            host.Connect(endpoint);
            BotClient client = new(i, 1, endpoint);
            clients.Add(client);
            agents.Add(new BotAgent(new BotConfig(i, 1000 + i, 1)));
        }

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            int maxConnectSteps = 2000;
            int steps = 0;
            while (!client.HandshakeDone)
            {
                if (steps++ > maxConnectSteps)
                {
                    throw new InvalidOperationException($"handshake failed bot={client.BotIndex}");
                }

                cts.Token.ThrowIfCancellationRequested();
                foreach (BotClient c in clients.OrderBy(c => c.BotIndex))
                {
                    c.PumpMessages(_ => { });
                    if (c.HasWelcome)
                    {
                        c.EnterZone();
                    }
                }

                host.ProcessInboundOnce();
            }
        }

        foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
        {
            client.PumpMessages(_ => { });
        }

        int[] maxSeenTick = new int[clients.Count];
        long initialNpcHp = 0;
        bool sawInitial = false;

        for (int tick = 1; tick <= 180; tick++)
        {
            foreach (BotClient client in clients.OrderBy(c => c.BotIndex))
            {
                client.PumpMessages(message =>
                {
                    if (message is Snapshot snapshot)
                    {
                        Assert.True(snapshot.Tick >= maxSeenTick[client.BotIndex]);
                        maxSeenTick[client.BotIndex] = snapshot.Tick;

                        if (!sawInitial)
                        {
                            initialNpcHp = snapshot.Entities.Where(e => e.Kind == SnapshotEntityKind.Npc && e.Hp > 0).Sum(e => (long)e.Hp);
                            sawInitial = true;
                        }
                    }
                });

                BotDecision decision = agents[client.BotIndex].Decide(client);
                int commandTick = Math.Max(tick, client.LastSnapshot?.Tick ?? tick);
                client.SendInput(commandTick, decision.MoveX, decision.MoveY);

                if (decision.AttackTargetId is int targetId && client.EntityId is int attackerId)
                {
                    client.SendAttackIntent(commandTick, attackerId, targetId);
                }
            }

            host.StepOnce();
        }

        foreach (BotClient client in clients)
        {
            Assert.True(client.SnapshotsReceived > 0);
            Assert.True(client.EntityId is not null);
            Assert.True(client.LastSnapshotTick > 0);
            Assert.NotNull(client.LastSnapshot);
        }

        long finalNpcHp = clients[0].LastSnapshot!.Entities
            .Where(e => e.Kind == SnapshotEntityKind.Npc && e.Hp > 0)
            .Sum(e => (long)e.Hp);

        Assert.True(sawInitial);
        Assert.True(finalNpcHp < initialNpcHp || finalNpcHp == 0);
    }
}
