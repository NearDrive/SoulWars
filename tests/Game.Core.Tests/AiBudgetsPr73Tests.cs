using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR73")]
public sealed class PathBudgetLimitTests
{
    [Fact]
    public void ZeroPathExpansionBudget_ProducesDeterministicHoldResult()
    {
        SimulationConfig config = Pr73TestHelpers.CreateConfig(7301, new AiBudgetConfig(0, 4, 16));
        WorldState baseline = RunScenario(config);
        WorldState secondRun = RunScenario(config);

        EntityState npcA = GetNpc(baseline);
        EntityState npcB = GetNpc(secondRun);

        Assert.Equal(MoveIntentType.Hold, npcA.MoveIntent.Type);
        Assert.Equal(0, npcA.MoveIntent.PathLen);
        Assert.Equal(StateChecksum.ComputeGlobalChecksum(baseline), StateChecksum.ComputeGlobalChecksum(secondRun));
        Assert.Equal(npcA.MoveIntent, npcB.MoveIntent);
    }

    private static WorldState RunScenario(SimulationConfig config)
    {
        WorldState state = Simulation.CreateInitialState(config, Pr73TestHelpers.BuildZone(npcCount: 1));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(14), Fix32.FromInt(14))))));
        return state;
    }

    private static EntityState GetNpc(WorldState state)
        => Assert.Single(state.Zones[0].Entities.Where(e => e.Kind == EntityKind.Npc));
}

[Trait("Category", "PR73")]
public sealed class AiDecisionBudgetTests
{
    [Fact]
    public void RepathBudget_AllowsLowestEntityIdsFirst()
    {
        SimulationConfig config = Pr73TestHelpers.CreateConfig(7302, new AiBudgetConfig(2048, 3, 64));
        WorldState state = Simulation.CreateInitialState(config, Pr73TestHelpers.BuildZone(npcCount: 10));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(14), Fix32.FromInt(14))))));

        ImmutableArray<EntityState> npcs = state.Zones[0].Entities
            .Where(e => e.Kind == EntityKind.Npc)
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray();

        Assert.Equal(10, npcs.Length);

        int repathed = npcs.Count(n => n.MoveIntent.PathLen > 0 && n.MoveIntent.NextRepathTick > 1);
        Assert.Equal(3, repathed);

        foreach (EntityState npc in npcs.Take(3))
        {
            Assert.True(npc.MoveIntent.PathLen > 0);
            Assert.True(npc.MoveIntent.NextRepathTick > 1);
        }

        foreach (EntityState npc in npcs.Skip(3))
        {
            Assert.Equal(0, npc.MoveIntent.PathLen);
            Assert.Equal(1, npc.MoveIntent.NextRepathTick);
        }
    }
}

[Trait("Category", "PR73")]
public sealed class DeterministicFallbackTests
{
    [Fact]
    public void ZeroDecisionBudget_HoldsPreviousIntentDeterministically()
    {
        SimulationConfig warmupConfig = Pr73TestHelpers.CreateConfig(7303, new AiBudgetConfig(4096, 32, 32));
        WorldState state = Simulation.CreateInitialState(warmupConfig, Pr73TestHelpers.BuildZone(npcCount: 1));

        state = Simulation.Step(warmupConfig, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(14), Fix32.FromInt(14))))));

        EntityState warmNpc = GetNpc(state);
        Assert.True(warmNpc.MoveIntent.PathLen > 0);

        SimulationConfig frozenConfig = warmupConfig with { AiBudgets = new AiBudgetConfig(0, 0, 0) };

        WorldState runA = Simulation.Step(frozenConfig, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        WorldState runB = Simulation.Step(frozenConfig, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        EntityState npcA = GetNpc(runA);
        EntityState npcB = GetNpc(runB);

        Assert.Equal(warmNpc.MoveIntent, npcA.MoveIntent);
        Assert.Equal(npcA.MoveIntent, npcB.MoveIntent);
        Assert.Equal(StateChecksum.ComputeGlobalChecksum(runA), StateChecksum.ComputeGlobalChecksum(runB));
    }

    private static EntityState GetNpc(WorldState state)
        => Assert.Single(state.Zones[0].Entities.Where(e => e.Kind == EntityKind.Npc));
}

file static class Pr73TestHelpers
{
    public static ZoneDefinitions BuildZone(int npcCount)
    {
        ImmutableArray<Vec2Fix>.Builder spawns = ImmutableArray.CreateBuilder<Vec2Fix>(npcCount);
        for (int i = 0; i < npcCount; i++)
        {
            spawns.Add(new Vec2Fix(Fix32.FromInt(1), Fix32.FromInt(1 + i)));
        }

        ZoneDefinition zone = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(16), Fix32.FromInt(16)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("npc.default", npcCount, npcCount, spawns.ToImmutable())),
            null,
            null,
            ImmutableArray<EncounterDefinition>.Empty);

        return new ZoneDefinitions(ImmutableArray.Create(zone));
    }

    public static SimulationConfig CreateConfig(int seed, AiBudgetConfig budgets)
        => new(
            Seed: seed,
            TickHz: 20,
            DtFix: new Fix32(3277),
            MoveSpeed: Fix32.FromInt(4),
            MaxSpeed: Fix32.FromInt(4),
            Radius: new Fix32(16384),
            ZoneCount: 1,
            MapWidth: 16,
            MapHeight: 16,
            NpcCountPerZone: 0,
            NpcWanderPeriodTicks: 9999,
            NpcAggroRange: Fix32.FromInt(64),
            SkillDefinitions: ImmutableArray<SkillDefinition>.Empty,
            AiBudgets: budgets,
            Invariants: InvariantOptions.Enabled);
}
