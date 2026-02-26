using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

public sealed class FogOfWarReplayVerifyTests
{
    [Fact]
    [Trait("Category", "PR80")]
    [Trait("Category", "ReplayVerify")]
    [Trait("Category", "Canary")]
    public void ReplayVerify_FullFogOfWarCanary_NoDivergence_AndVisibilityTransitionsHold()
    {
        FogOfWarScenarioRun baseline = FogOfWarPr80Scenario.Run(restartTick: null);
        FogOfWarScenarioRun replay = FogOfWarPr80Scenario.Run(restartTick: null);

        Assert.Equal(240, baseline.TickChecksums.Length);
        Assert.Equal(240, replay.TickChecksums.Length);

        int divergentTick = FindFirstDivergentTick(baseline.TickChecksums, replay.TickChecksums);
        Assert.Equal(-1, divergentTick);

        Assert.True(baseline.HiddenEntityCastRejected, "Entity-target cast should be rejected before target is visible.");
        Assert.True(baseline.HiddenPointAoeAccepted, "Point/AoE cast should be accepted even while target entity is hidden.");
        Assert.True(baseline.VisibleEntityCastAccepted, "Entity-target cast should be accepted after faction vision gains LoS.");
        Assert.True(baseline.VisibilityWasGainedBeforeFinalCast, "Faction shared vision should reveal target before final entity-target cast.");
        Assert.True(baseline.NoDamageOnHiddenEntityCast, "Hidden entity-target cast should not apply damage.");

        Assert.Equal(baseline.FinalChecksum, replay.FinalChecksum);
        Assert.Equal(baseline.FinalGlobalChecksum, replay.FinalGlobalChecksum);
    }

    private static int FindFirstDivergentTick(ImmutableArray<string> expected, ImmutableArray<string> actual)
    {
        int minLength = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < minLength; i++)
        {
            if (!string.Equals(expected[i], actual[i], System.StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return expected.Length == actual.Length ? -1 : minLength + 1;
    }
}

public sealed class FogOfWarRestartDeterminismTests
{
    [Fact]
    [Trait("Category", "PR80")]
    public void SnapshotRestartAtTick120_MatchesNoRestart_FinalChecksums_AndVisibilityState()
    {
        FogOfWarScenarioRun baseline = FogOfWarPr80Scenario.Run(restartTick: null);
        FogOfWarScenarioRun resumed = FogOfWarPr80Scenario.Run(restartTick: 120);

        Assert.Equal(baseline.FinalChecksum, resumed.FinalChecksum);
        Assert.Equal(baseline.FinalGlobalChecksum, resumed.FinalGlobalChecksum);
        Assert.Equal(baseline.FinalTargetHp, resumed.FinalTargetHp);
        Assert.Equal(baseline.VisibilityTicks, resumed.VisibilityTicks);

        Assert.True(resumed.VisibilityWasGainedBeforeFinalCast, "Visibility progression should remain stable after restart.");
        Assert.True(resumed.HiddenEntityCastRejected);
        Assert.True(resumed.HiddenPointAoeAccepted);
        Assert.True(resumed.VisibleEntityCastAccepted);
    }
}

internal readonly record struct FogOfWarScenarioRun(
    string FinalChecksum,
    string FinalGlobalChecksum,
    ImmutableArray<string> TickChecksums,
    ImmutableArray<int> VisibilityTicks,
    int FinalTargetHp,
    bool HiddenEntityCastRejected,
    bool HiddenPointAoeAccepted,
    bool VisibleEntityCastAccepted,
    bool NoDamageOnHiddenEntityCast,
    bool VisibilityWasGainedBeforeFinalCast);

internal static class FogOfWarPr80Scenario
{
    private static readonly ZoneId ZoneId = new(1);
    private static readonly EntityId BotA1 = new(801);
    private static readonly EntityId BotA2 = new(802);
    private static readonly EntityId BotB1 = new(901);
    private static readonly FactionId FactionA = new(1);
    private static readonly FactionId FactionB = new(2);

    public static FogOfWarScenarioRun Run(int? restartTick)
    {
        SimulationConfig config = CreateConfig();
        WorldState state = BuildWorld();

        ImmutableArray<string>.Builder tickChecksums = ImmutableArray.CreateBuilder<string>(240);
        ImmutableArray<int>.Builder visibilityTicks = ImmutableArray.CreateBuilder<int>();

        bool hiddenEntityCastRejected = false;
        bool hiddenPointAoeAccepted = false;
        bool visibleEntityCastAccepted = false;
        bool noDamageOnHiddenEntityCast = false;
        bool visibilityWasGainedBeforeFinalCast = false;

        for (int tick = 0; tick < 240; tick++)
        {
            ZoneState zoneBefore = SingleZone(state);
            int hpBefore = EntityById(zoneBefore, BotB1).Hp;

            ImmutableArray<WorldCommand> commands = BuildCommandsForTick(tick)
                .OrderBy(c => c.EntityId.Value)
                .ThenBy(c => c.TargetEntityId?.Value ?? int.MaxValue)
                .ToImmutableArray();

            state = Simulation.Step(config, state, new Inputs(commands));

            ZoneState zoneAfter = SingleZone(state);
            bool visibleToFactionA = zoneAfter.Visibility.IsVisible(FactionA, 7, 3);
            if (visibleToFactionA)
            {
                visibilityTicks.Add(tick + 1);
            }

            int hpAfter = EntityById(zoneAfter, BotB1).Hp;

            if (tick == 5)
            {
                hiddenEntityCastRejected = state.SkillCastIntents.IsEmpty;
                noDamageOnHiddenEntityCast = hpAfter == hpBefore &&
                                             state.CombatEvents.All(e => !(e.SourceId == BotA1 && e.TargetId == BotB1 && e.Amount > 0));
            }

            if (tick == 10)
            {
                hiddenPointAoeAccepted = state.SkillCastIntents.Any(i => i.CasterId == BotA1 && i.TargetKind == CastTargetKind.Point) &&
                                         hpAfter < hpBefore;
            }

            if (tick == 140)
            {
                visibleEntityCastAccepted = state.SkillCastIntents.Any(i => i.CasterId == BotA1 && i.TargetKind == CastTargetKind.Entity && i.TargetEntityId == BotB1) &&
                                            hpAfter < hpBefore;
                visibilityWasGainedBeforeFinalCast = visibilityTicks.Any(t => t <= tick);
            }

            tickChecksums.Add(StateChecksum.ComputeGlobalChecksum(state));

            if (restartTick.HasValue && tick + 1 == restartTick.Value)
            {
                byte[] snapshot = WorldStateSerializer.SaveToBytes(state);
                state = WorldStateSerializer.LoadFromBytes(snapshot);
            }
        }

        return new FogOfWarScenarioRun(
            FinalChecksum: StateChecksum.Compute(state),
            FinalGlobalChecksum: StateChecksum.ComputeGlobalChecksum(state),
            TickChecksums: tickChecksums.ToImmutable(),
            VisibilityTicks: visibilityTicks.Distinct().ToImmutableArray(),
            FinalTargetHp: EntityById(SingleZone(state), BotB1).Hp,
            HiddenEntityCastRejected: hiddenEntityCastRejected,
            HiddenPointAoeAccepted: hiddenPointAoeAccepted,
            VisibleEntityCastAccepted: visibleEntityCastAccepted,
            NoDamageOnHiddenEntityCast: noDamageOnHiddenEntityCast,
            VisibilityWasGainedBeforeFinalCast: visibilityWasGainedBeforeFinalCast);
    }

    private static ImmutableArray<WorldCommand> BuildCommandsForTick(int tick)
    {
        ImmutableArray<WorldCommand>.Builder commands = ImmutableArray.CreateBuilder<WorldCommand>();

        if (tick == 5)
        {
            commands.Add(EntityCast(BotA1, skillId: 10, BotB1));
        }

        if (tick == 10)
        {
            commands.Add(PointCast(BotA1, skillId: 11, x: 7, y: 3));
        }

        if (tick is >= 20 and <= 24)
        {
            commands.Add(Move(BotA2, moveX: 0, moveY: -1));
        }
        else if (tick is >= 25 and <= 44)
        {
            commands.Add(Move(BotA2, moveX: 1, moveY: 0));
        }
        else if (tick is >= 45 and <= 59)
        {
            commands.Add(Move(BotA2, moveX: 0, moveY: 1));
        }

        if (tick == 140)
        {
            commands.Add(EntityCast(BotA1, skillId: 10, BotB1));
        }

        return commands.ToImmutable();
    }

    private static WorldCommand Move(EntityId entityId, sbyte moveX, sbyte moveY)
        => new(
            Kind: WorldCommandKind.MoveIntent,
            EntityId: entityId,
            ZoneId: ZoneId,
            MoveX: moveX,
            MoveY: moveY);

    private static WorldCommand EntityCast(EntityId casterId, int skillId, EntityId targetId)
        => new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: casterId,
            ZoneId: ZoneId,
            SkillId: new SkillId(skillId),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: targetId);

    private static WorldCommand PointCast(EntityId casterId, int skillId, int x, int y)
        => new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: casterId,
            ZoneId: ZoneId,
            SkillId: new SkillId(skillId),
            TargetKind: CastTargetKind.Point,
            TargetPosXRaw: Fix32.FromInt(x).Raw,
            TargetPosYRaw: Fix32.FromInt(y).Raw);

    private static ZoneState SingleZone(WorldState state) => Assert.Single(state.Zones);

    private static EntityState EntityById(ZoneState zone, EntityId id)
        => Assert.Single(zone.Entities.Where(e => e.Id == id));

    private static WorldState BuildWorld()
    {
        TileMap map = BuildMap(
            width: 10,
            height: 7,
            blockedTiles:
            [
                (4, 1),
                (4, 2),
                (4, 3),
                (4, 4),
                (4, 5)
            ]);

        ImmutableArray<EntityState> entities = ImmutableArray.Create(
            CreateEntity(BotA1, x: 2, y: 3, factionId: FactionA, visionRadius: 3),
            CreateEntity(BotA2, x: 2, y: 1, factionId: FactionA, visionRadius: 4),
            CreateEntity(BotB1, x: 7, y: 3, factionId: FactionB, visionRadius: 3))
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray();

        ZoneState zone = new(ZoneId, map, entities);

        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: entities.Select(e => new EntityLocation(e.Id, ZoneId)).ToImmutableArray(),
            LootEntities: ImmutableArray<LootEntityState>.Empty);
    }

    private static TileMap BuildMap(int width, int height, params (int X, int Y)[] blockedTiles)
    {
        TileKind[] tiles = Enumerable.Repeat(TileKind.Empty, width * height).ToArray();
        for (int i = 0; i < blockedTiles.Length; i++)
        {
            (int x, int y) = blockedTiles[i];
            tiles[(y * width) + x] = TileKind.Solid;
        }

        return new TileMap(width, height, tiles.ToImmutableArray());
    }

    private static EntityState CreateEntity(EntityId id, int x, int y, FactionId factionId, int visionRadius)
        => new(
            Id: id,
            Pos: new Vec2Fix(Fix32.FromInt(x), Fix32.FromInt(y)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 5,
            AttackCooldownTicks: 1,
            LastAttackTick: -1,
            Kind: EntityKind.Player,
            FactionId: factionId,
            VisionRadiusTiles: visionRadius);

    private static SimulationConfig CreateConfig()
        => SimulationConfig.Default(seed: 8013) with
        {
            NpcCountPerZone = 0,
            MapWidth = 10,
            MapHeight = 7,
            SkillDefinitions = ImmutableArray.Create(
                new SkillDefinition(new SkillId(10), Fix32.FromInt(8).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 6, ResourceCost: 0, TargetKind: CastTargetKind.Entity, BaseAmount: 5),
                new SkillDefinition(new SkillId(11), Fix32.FromInt(8).Raw, HitRadiusRaw: Fix32.FromInt(2).Raw, CooldownTicks: 6, ResourceCost: 0, TargetKind: CastTargetKind.Point, BaseAmount: 7))
        };
}
