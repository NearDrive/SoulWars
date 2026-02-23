using System;
using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

public sealed class MovingBossReplayVerifyTests
{
    [Fact]
    [Trait("Category", "PR74")]
    [Trait("Category", "ReplayVerify")]
    [Trait("Category", "Canary")]
    public void ReplayVerify_MovingBoss_Canary_HasNoDivergence_AndExpectedBehavior()
    {
        SimulationConfig config = MovingBossCanaryScenario.CreateConfig();

        ScenarioRun baseline = MovingBossCanaryScenario.Run(config, restartTick: null);
        ScenarioRun replay = MovingBossCanaryScenario.Run(config, restartTick: null);

        Assert.Equal(300, baseline.TickChecksums.Length);
        Assert.Equal(300, replay.TickChecksums.Length);
        Assert.Equal(baseline.FinalChecksum, replay.FinalChecksum);

        Assert.True(baseline.SawChaseToTank, "Expected boss to chase tank at least once.");
        Assert.True(baseline.SawChaseToDps, "Expected boss to retarget to DPS burst at least once.");
        Assert.True(baseline.SawLeashing, "Expected leash/reset state to occur during kite phase.");
        Assert.True(baseline.SawPathComputed, "Expected pathfinding to produce at least one path.");
        Assert.True(baseline.SawBudgetDeferredRepath, "Expected AI repath throttling under tight budgets.");
        Assert.True(baseline.FinalBossWithinAnchorRadius, "Expected final boss position to be within leash anchor radius after reset.");
    }
}

public sealed class MovingBossRestartDeterminismTests
{
    [Fact]
    [Trait("Category", "PR74")]
    public void MovingBoss_RestartAtTick150_MatchesNoRestart_FinalChecksum()
    {
        SimulationConfig config = MovingBossCanaryScenario.CreateConfig();

        ScenarioRun baseline = MovingBossCanaryScenario.Run(config, restartTick: null);
        ScenarioRun resumed = MovingBossCanaryScenario.Run(config, restartTick: 150);

        Assert.Equal(baseline.FinalChecksum, resumed.FinalChecksum);
    }
}

internal readonly record struct ScenarioRun(
    string FinalChecksum,
    ImmutableArray<string> TickChecksums,
    bool SawChaseToTank,
    bool SawChaseToDps,
    bool SawLeashing,
    bool SawPathComputed,
    bool SawBudgetDeferredRepath,
    bool FinalBossWithinAnchorRadius);

internal static class MovingBossCanaryScenario
{
    private static readonly ZoneId ZoneId = new(1);
    private static readonly EntityId TankId = new(401);
    private static readonly EntityId DpsId = new(402);
    private static readonly EntityId SupportId = new(403);

    public static ScenarioRun Run(SimulationConfig config, int? restartTick)
    {
        WorldState state = Simulation.CreateInitialState(config, CreateZoneDefinitions());
        state = EnterPartyMembers(state, config);

        const int totalTicks = 300;
        ImmutableArray<string>.Builder tickChecksums = ImmutableArray.CreateBuilder<string>(totalTicks);

        bool sawChaseToTank = false;
        bool sawChaseToDps = false;
        bool sawLeashing = false;
        bool sawPathComputed = false;
        bool sawBudgetDeferredRepath = false;

        for (int tick = 0; tick < totalTicks; tick++)
        {
            state = Simulation.Step(config, state, new Inputs(BuildBotCommands(state, tick)));
            tickChecksums.Add(StateChecksum.ComputeGlobalChecksum(state));

            if (TryGetBoss(state, out EntityState boss))
            {
                sawChaseToTank |= boss.MoveIntent.Type == MoveIntentType.ChaseEntity && boss.MoveIntent.TargetEntityId == TankId;
                sawChaseToDps |= boss.MoveIntent.Type == MoveIntentType.ChaseEntity && boss.MoveIntent.TargetEntityId == DpsId;
                sawLeashing |= boss.Leash.IsLeashing;
                sawPathComputed |= boss.MoveIntent.PathLen > 0;
                sawBudgetDeferredRepath |= boss.MoveIntent.NextRepathTick > state.Tick + 1;
            }

            if (restartTick.HasValue && tick + 1 == restartTick.Value)
            {
                byte[] snapshot = WorldStateSerializer.SaveToBytes(state);
                state = WorldStateSerializer.LoadFromBytes(snapshot);
            }
        }

        Assert.True(TryGetBoss(state, out EntityState finalBoss), "Boss should exist at end of run.");

        return new ScenarioRun(
            FinalChecksum: StateChecksum.ComputeGlobalChecksum(state),
            TickChecksums: tickChecksums.ToImmutable(),
            SawChaseToTank: sawChaseToTank,
            SawChaseToDps: sawChaseToDps,
            SawLeashing: sawLeashing,
            SawPathComputed: sawPathComputed,
            SawBudgetDeferredRepath: sawBudgetDeferredRepath,
            FinalBossWithinAnchorRadius: IsWithinAnchorRadius(finalBoss));
    }

    public static SimulationConfig CreateConfig() => new(
        Seed: 7401,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 32,
        MapHeight: 32,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 9999,
        NpcAggroRange: Fix32.FromInt(64),
        SkillDefinitions: ImmutableArray.Create(
            new SkillDefinition(new SkillId(1), Fix32.FromInt(64).Raw, 0, 1, CooldownTicks: 12, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, CastTargetKind.Entity, BaseAmount: 1),
            new SkillDefinition(new SkillId(2), Fix32.FromInt(64).Raw, 0, 1, CooldownTicks: 30, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, CastTargetKind.Entity, BaseAmount: 8),
            new SkillDefinition(new SkillId(3), Fix32.FromInt(64).Raw, 0, 1, CooldownTicks: 15, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, CastTargetKind.Entity, BaseAmount: 1)),
        AiBudgets: new AiBudgetConfig(MaxPathExpansionsPerTick: 192, MaxRepathsPerTick: 1, MaxAiDecisionsPerTick: 8),
        Invariants: InvariantOptions.Enabled);

    private static ZoneDefinitions CreateZoneDefinitions()
    {
        EncounterDefinition encounter = new(
            new EncounterId(7401),
            "moving-boss-canary",
            Version: 1,
            ZoneId,
            ImmutableArray.Create(
                new EncounterPhaseDefinition(ImmutableArray.Create(
                    new EncounterTriggerDefinition(
                        EncounterTriggerKind.OnTick,
                        AtTickOffset: 1,
                        Actions: ImmutableArray.Create(new EncounterActionDefinition(
                            EncounterActionKind.SpawnNpc,
                            NpcArchetypeId: "boss",
                            X: Fix32.FromInt(6),
                            Y: Fix32.FromInt(6),
                            Count: 1)))))));

        Fix32 half = new(Fix32.OneRaw / 2);
        ImmutableArray<ZoneAabb> maze = ImmutableArray.Create(
            new ZoneAabb(new Vec2Fix(Fix32.FromInt(10) + half, Fix32.FromInt(11) + half), new Vec2Fix(half, Fix32.FromInt(11) + half)),
            new ZoneAabb(new Vec2Fix(Fix32.FromInt(20) + half, Fix32.FromInt(21) + half), new Vec2Fix(half, Fix32.FromInt(11) + half)),
            new ZoneAabb(new Vec2Fix(Fix32.FromInt(15), Fix32.FromInt(10) + half), new Vec2Fix(Fix32.FromInt(5), half)));

        ZoneDefinition zone = new(
            ZoneId,
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(32), Fix32.FromInt(32)),
            maze,
            ImmutableArray<NpcSpawnDefinition>.Empty,
            null,
            null,
            ImmutableArray.Create(encounter));

        return new ZoneDefinitions(ImmutableArray.Create(zone));
    }

    private static WorldState EnterPartyMembers(WorldState state, SimulationConfig config)
    {
        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, TankId, ZoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(8), Fix32.FromInt(8))),
            new WorldCommand(WorldCommandKind.EnterZone, DpsId, ZoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(14), Fix32.FromInt(8))),
            new WorldCommand(WorldCommandKind.EnterZone, SupportId, ZoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(9), Fix32.FromInt(9))))));
    }

    private static ImmutableArray<WorldCommand> BuildBotCommands(WorldState state, int tick)
    {
        if (!TryGetBossId(state, out EntityId bossId))
        {
            return ImmutableArray<WorldCommand>.Empty;
        }

        ImmutableArray<WorldCommand>.Builder commands = ImmutableArray.CreateBuilder<WorldCommand>();

        if (tick < 220 && tick % 4 == 0)
        {
            commands.Add(new WorldCommand(WorldCommandKind.CastSkill, TankId, ZoneId, TargetEntityId: bossId, SkillId: new SkillId(1), TargetKind: CastTargetKind.Entity));
        }

        if (tick is 45 or 75 or 105 or 135 or 165)
        {
            commands.Add(new WorldCommand(WorldCommandKind.CastSkill, DpsId, ZoneId, TargetEntityId: bossId, SkillId: new SkillId(2), TargetKind: CastTargetKind.Entity));
        }

        if (tick % 30 == 0)
        {
            commands.Add(new WorldCommand(WorldCommandKind.CastSkill, SupportId, ZoneId, TargetEntityId: bossId, SkillId: new SkillId(3), TargetKind: CastTargetKind.Entity));
        }

        (sbyte moveX, sbyte moveY) tankMove = GetTankMove(tick);
        commands.Add(new WorldCommand(WorldCommandKind.MoveIntent, TankId, ZoneId, MoveX: tankMove.moveX, MoveY: tankMove.moveY));

        commands.Add(new WorldCommand(WorldCommandKind.MoveIntent, DpsId, ZoneId, MoveX: (sbyte)(tick < 160 ? 1 : 0), MoveY: 0));
        commands.Add(new WorldCommand(WorldCommandKind.MoveIntent, SupportId, ZoneId, MoveX: 0, MoveY: (sbyte)(tick < 40 ? 1 : 0)));

        return commands.ToImmutable();
    }

    private static (sbyte moveX, sbyte moveY) GetTankMove(int tick)
    {
        if (tick < 30)
        {
            return (1, 0);
        }

        if (tick < 80)
        {
            return (0, 1);
        }

        if (tick < 150)
        {
            return (1, 0);
        }

        if (tick < 210)
        {
            return (0, 1);
        }

        if (tick < 260)
        {
            return (-1, -1);
        }

        return (0, 0);
    }

    private static bool TryGetBossId(WorldState state, out EntityId bossId)
    {
        bossId = default;
        if (!state.TryGetZone(ZoneId, out ZoneState zone))
        {
            return false;
        }

        bossId = zone.Entities
            .Where(e => e.Kind == EntityKind.Npc)
            .OrderBy(e => e.Id.Value)
            .Select(e => e.Id)
            .FirstOrDefault();

        return bossId.Value > 0;
    }

    private static bool TryGetBoss(WorldState state, out EntityState boss)
    {
        boss = null!;
        if (!state.TryGetZone(ZoneId, out ZoneState zone))
        {
            return false;
        }

        EntityState? candidate = zone.Entities
            .Where(e => e.Kind == EntityKind.Npc)
            .OrderBy(e => e.Id.Value)
            .FirstOrDefault();

        if (candidate is null)
        {
            return false;
        }

        boss = candidate;
        return true;
    }

    private static bool IsWithinAnchorRadius(EntityState npc)
    {
        Fix32 dx = npc.Pos.X - npc.Leash.AnchorX;
        Fix32 dy = npc.Pos.Y - npc.Leash.AnchorY;
        Fix32 distSq = (dx * dx) + (dy * dy);
        return distSq <= npc.Leash.RadiusSq;
    }
}
