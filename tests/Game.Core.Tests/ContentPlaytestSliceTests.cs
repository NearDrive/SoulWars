using System.Collections.Immutable;
using Game.Core;

namespace Game.Core.Tests;

public sealed class ContentPlaytestSliceTests
{
    [Fact]
    public void StarterDeck_IsLoadedForRun()
    {
        PlaytestContentSlice slice = ContentPlaytestSlice.CreateDeterministic();

        Assert.Equal("Blades", slice.Archetype);
        Assert.Equal(10, slice.StarterDeck.Length);
        Assert.Equal(5, slice.StarterDeck.Count(id => id == "blades_strike"));
        Assert.Equal(3, slice.StarterDeck.Count(id => id == "blades_guard"));
        Assert.Equal(2, slice.StarterDeck.Count(id => id == "blades_quick_draw"));
    }

    [Fact]
    public void CombatNode_UsesStandardEnemy()
    {
        PlaytestContentSlice slice = ContentPlaytestSlice.CreateDeterministic();

        PlaytestEnemyDefinition enemy = slice.EnemyForNode(PlaytestNodeType.Combat);
        Assert.Equal("enemy_raider", enemy.Id);
    }

    [Fact]
    public void EliteNode_UsesEliteEnemy()
    {
        PlaytestContentSlice slice = ContentPlaytestSlice.CreateDeterministic();

        PlaytestEnemyDefinition enemy = slice.EnemyForNode(PlaytestNodeType.Elite);
        Assert.Equal("enemy_sentinel", enemy.Id);
    }

    [Fact]
    public void BossNode_UsesBossEnemy()
    {
        PlaytestContentSlice slice = ContentPlaytestSlice.CreateDeterministic();

        PlaytestEnemyDefinition enemy = slice.EnemyForNode(PlaytestNodeType.Boss);
        Assert.Equal("enemy_warlord", enemy.Id);
    }

    [Fact]
    public void RewardCards_ComeFromConfiguredPool()
    {
        PlaytestContentSlice slice = ContentPlaytestSlice.CreateDeterministic();

        HashSet<string> configuredIds = slice.Cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(slice.RewardCardPool);
        Assert.All(slice.RewardCardPool, id => Assert.Contains(id, configuredIds));
    }

    [Fact]
    public void ContentSlice_IsDeterministic()
    {
        PlaytestContentSlice first = ContentPlaytestSlice.CreateDeterministic();
        PlaytestContentSlice second = ContentPlaytestSlice.CreateDeterministic();

        Assert.Equal(first, second);
        Assert.Equal(
            first.Cards.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray(),
            second.Cards.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray());
        Assert.Equal(first.StarterDeck, second.StarterDeck);
        Assert.Equal(first.Encounters, second.Encounters);
        Assert.Equal(first.RewardCardPool, second.RewardCardPool);
    }
}
