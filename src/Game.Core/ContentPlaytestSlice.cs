using System.Collections.Immutable;

namespace Game.Core;

public enum PlaytestNodeType : byte
{
    Combat = 1,
    Elite = 2,
    Boss = 3
}

public enum PlaytestCardEffectKind : byte
{
    Attack = 1,
    Armor = 2,
    Draw = 3,
    Utility = 4
}

public sealed record PlaytestCardDefinition(
    string Id,
    string Name,
    int Cost,
    PlaytestCardEffectKind Effect,
    int Magnitude,
    int Extra = 0);

public sealed record PlaytestEnemyDefinition(
    string Id,
    string Name,
    int Hp,
    ImmutableArray<string> BehaviorDeck,
    string Role);

public sealed record PlaytestEncounterDefinition(
    PlaytestNodeType NodeType,
    string EnemyId);

public sealed record PlaytestContentSlice(
    string Archetype,
    ImmutableArray<PlaytestCardDefinition> Cards,
    ImmutableArray<string> StarterDeck,
    ImmutableArray<PlaytestEnemyDefinition> Enemies,
    ImmutableArray<PlaytestEncounterDefinition> Encounters,
    ImmutableArray<string> RewardCardPool)
{
    public PlaytestEnemyDefinition EnemyForNode(PlaytestNodeType nodeType)
    {
        string enemyId = Encounters.Single(e => e.NodeType == nodeType).EnemyId;
        return Enemies.Single(e => string.Equals(e.Id, enemyId, StringComparison.Ordinal));
    }
}

public static class ContentPlaytestSlice
{
    public static PlaytestContentSlice CreateDeterministic() => new(
        Archetype: "Blades",
        Cards: ImmutableArray.Create(
            new PlaytestCardDefinition("blades_strike", "Strike", Cost: 1, Effect: PlaytestCardEffectKind.Attack, Magnitude: 6),
            new PlaytestCardDefinition("blades_guard", "Guard", Cost: 1, Effect: PlaytestCardEffectKind.Armor, Magnitude: 5),
            new PlaytestCardDefinition("blades_quick_draw", "Quick Draw", Cost: 1, Effect: PlaytestCardEffectKind.Draw, Magnitude: 2),
            new PlaytestCardDefinition("blades_heavy_attack", "Heavy Attack", Cost: 2, Effect: PlaytestCardEffectKind.Attack, Magnitude: 12),
            new PlaytestCardDefinition("blades_feint", "Feint", Cost: 0, Effect: PlaytestCardEffectKind.Utility, Magnitude: 0, Extra: 1))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToImmutableArray(),
        StarterDeck: ImmutableArray.CreateRange(Enumerable.Repeat("blades_strike", 5)
            .Concat(Enumerable.Repeat("blades_guard", 3))
            .Concat(Enumerable.Repeat("blades_quick_draw", 2))),
        Enemies: ImmutableArray.Create(
            new PlaytestEnemyDefinition(
                "enemy_raider",
                "Raider",
                Hp: 30,
                BehaviorDeck: ImmutableArray.Create("jab", "jab", "brace"),
                Role: "Standard pressure attacker"),
            new PlaytestEnemyDefinition(
                "enemy_sentinel",
                "Sentinel",
                Hp: 55,
                BehaviorDeck: ImmutableArray.Create("guarded_strike", "brace", "guarded_strike"),
                Role: "Elite bruiser with sustain"),
            new PlaytestEnemyDefinition(
                "enemy_warlord",
                "Warlord",
                Hp: 120,
                BehaviorDeck: ImmutableArray.Create("commanding_blow", "fortify", "commanding_blow", "overhead_crush"),
                Role: "Boss attrition finisher"))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToImmutableArray(),
        Encounters: ImmutableArray.Create(
            new PlaytestEncounterDefinition(PlaytestNodeType.Combat, "enemy_raider"),
            new PlaytestEncounterDefinition(PlaytestNodeType.Elite, "enemy_sentinel"),
            new PlaytestEncounterDefinition(PlaytestNodeType.Boss, "enemy_warlord")),
        RewardCardPool: ImmutableArray.Create("blades_guard", "blades_heavy_attack", "blades_quick_draw", "blades_strike"));
}
