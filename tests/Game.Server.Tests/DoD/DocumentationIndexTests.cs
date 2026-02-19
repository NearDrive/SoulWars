using System.Net;
using Game.BotRunner;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests.DoD;

[Trait("Category", "DoD")]
public sealed class DocumentationIndexTests
{
    [Fact]
    public void CanonicalDocs_Exist_And_RoadmapCoversMvp1ToMvp10()
    {
        string root = ResolveRepoRoot();

        string[] canonicalDocs =
        [
            "docs/protocol/mmo_2d_ragnarokish_pr_plan (2).md",
            "docs/protocol/MVP3_PR_PLAN (1).md",
            "docs/protocol/MVP4_PR_PLAN.md",
            "docs/protocol/MVP5_PR_PLAN.md",
            "docs/protocol/MVP6_PR_PLAN.md",
            "docs/protocol/SoulWars_MVP7_PR_PLAN (1).md",
            "docs/protocol/SoulWars_MVP8_Drift_Guardrails (1).md",
            "docs/protocol/MVP9_PR_PLAN (1).md",
            "docs/protocol/SoulWars_MVP10_MasterPlan (1).md"
        ];

        foreach (string relPath in canonicalDocs)
        {
            Assert.True(File.Exists(Path.Combine(root, relPath)), $"Missing canonical doc: {relPath}");
        }

        string roadmapPath = Path.Combine(root, "docs/protocol/ROADMAP_INDEX.md");
        Assert.True(File.Exists(roadmapPath), "ROADMAP_INDEX.md must exist.");

        string roadmap = File.ReadAllText(roadmapPath);
        for (int mvp = 1; mvp <= 10; mvp++)
        {
            Assert.Contains($"MVP{mvp}", roadmap, StringComparison.Ordinal);
        }

        Assert.Contains("docs/protocol/MVP5_PR_PLAN.md", roadmap, StringComparison.Ordinal);
        Assert.Contains("docs/protocol/MVP6_PR_PLAN.md", roadmap, StringComparison.Ordinal);
        Assert.Contains("docs/protocol/SoulWars_MVP10_MasterPlan (1).md", roadmap, StringComparison.Ordinal);
    }

    [Fact]
    public void Mvp10_CanonicalAndLegacy_Sentinels_AreExplicit()
    {
        string root = ResolveRepoRoot();
        string masterPath = Path.Combine(root, "docs/protocol/SoulWars_MVP10_MasterPlan (1).md");
        string legacyPath = Path.Combine(root, "docs/protocol/SoulWars_MVP10_PR_PLAN.md");

        Assert.True(File.Exists(masterPath), "Missing MVP10 MasterPlan.");
        Assert.True(File.Exists(legacyPath), "Missing MVP10 legacy PR plan.");

        string masterText = File.ReadAllText(masterPath);
        string legacyText = File.ReadAllText(legacyPath);

        Assert.Contains("CANONICAL", masterText, StringComparison.Ordinal);
        Assert.Contains("LEGACY", legacyText, StringComparison.Ordinal);
        Assert.Contains("SoulWars_MVP10_MasterPlan (1).md", legacyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransportContract_Tcp_HelloWelcomeEnterZone_Works()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await using ServerRuntime runtime = new();
        await runtime.StartAsync(ServerConfig.Default(seed: 904), IPAddress.Loopback, 0, cts.Token);

        await using HeadlessClient client = new();
        await client.ConnectAsync("127.0.0.1", runtime.BoundPort, cts.Token);

        client.Send(new HelloV2("dod-transport", "doc-gate"));
        _ = await WaitForMessageAsync<Welcome>(runtime, client, TimeSpan.FromSeconds(4));

        client.EnterZone(1);
        EnterZoneAck ack = await WaitForMessageAsync<EnterZoneAck>(runtime, client, TimeSpan.FromSeconds(4));
        Snapshot snapshot = await WaitForMessageAsync<Snapshot>(runtime, client, TimeSpan.FromSeconds(4), s => s.Entities.Any(e => e.EntityId == ack.EntityId));

        Assert.Equal(1, ack.ZoneId);
        Assert.Equal(1, snapshot.ZoneId);
    }

    private static string ResolveRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Game.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repo root containing Game.sln.");
    }

    private static async Task<T> WaitForMessageAsync<T>(
        ServerRuntime runtime,
        HeadlessClient client,
        TimeSpan timeout,
        Func<T, bool>? predicate = null)
        where T : class, IServerMessage
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            runtime.StepOnce();
            while (client.TryRead(out IServerMessage? message))
            {
                if (message is T typed && (predicate is null || predicate(typed)))
                {
                    return typed;
                }
            }

            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException($"Timed out waiting for message {typeof(T).Name}.");
    }
}
