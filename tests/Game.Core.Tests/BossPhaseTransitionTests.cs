using System;
using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class BossPhaseTransitionTests
{
    [Fact]
    [Trait("Category", "PR66")]
    public void Boss_Phase_Transition_Sequence_Is_Deterministic()
    {
        SimulationConfig config = EncounterTriggerTests_CreateConfig();
        ImmutableArray<string> trace = RunScenario(config);

        Assert.Equal("phase=0;flags=0", trace[0]);

        int firstPhaseOne = FindFirstIndex(trace, v => v.StartsWith("phase=1;", StringComparison.Ordinal));
        Assert.True(firstPhaseOne >= 0, "Expected encounter to transition from phase 0 to phase 1.");

        int firstPhaseOneTriggered = FindFirstIndex(trace, v => string.Equals(v, "phase=1;flags=1", StringComparison.Ordinal));
        Assert.True(firstPhaseOneTriggered > firstPhaseOne, "Expected phase-1 tick trigger to fire after entering phase 1.");

        for (int i = firstPhaseOneTriggered; i < trace.Length; i++)
        {
            Assert.Equal("phase=1;flags=1", trace[i]);
        }
    }


    private static int FindFirstIndex(ImmutableArray<string> trace, Func<string, bool> predicate)
    {
        for (int i = 0; i < trace.Length; i++)
        {
            if (predicate(trace[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static ImmutableArray<string> RunScenario(SimulationConfig config)
    {
        EncounterDefinition def = new(
            new EncounterId(6610),
            "boss-two-phase",
            1,
            new ZoneId(1),
            ImmutableArray.Create(
                new EncounterPhaseDefinition(ImmutableArray.Create(
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnHpBelowPct, Target: EntityRef.Boss, Pct: 70, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.SetPhase, PhaseIndex: 1))))),
                new EncounterPhaseDefinition(ImmutableArray.Create(
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnTick, AtTickOffset: 30, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.CastSkill, Caster: EntityRef.Boss, SkillId: new SkillId(1), Target: TargetSpec.Self)))))));

        ZoneDefinition zoneDef = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(32), Fix32.FromInt(32)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("boss", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))))),
            null,
            null,
            ImmutableArray.Create(def));

        WorldState state = Simulation.CreateInitialState(config, new ZoneDefinitions(ImmutableArray.Create(zoneDef)));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(new WorldCommand(WorldCommandKind.EnterZone, new EntityId(12), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))))));

        ImmutableArray<string>.Builder trace = ImmutableArray.CreateBuilder<string>();
        for (int i = 0; i < 45; i++)
        {
            ImmutableArray<WorldCommand> commands = i < 10
                ? ImmutableArray.Create(new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(12), new ZoneId(1), TargetEntityId: new EntityId(100001)))
                : ImmutableArray<WorldCommand>.Empty;
            state = Simulation.Step(config, state, new Inputs(commands));

            EncounterRuntimeState runtime = Assert.Single(state.EncounterRegistryOrEmpty.RuntimeStates);
            string flags = string.Concat(runtime.FiredTriggers.Select(f => f ? '1' : '0'));
            trace.Add($"phase={runtime.CurrentPhase};flags={flags}");
        }

        return trace.ToImmutable();
    }

    private static SimulationConfig EncounterTriggerTests_CreateConfig() => new(
        Seed: 66,
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
        NpcAggroRange: Fix32.Zero,
        SkillDefinitions: ImmutableArray.Create(new SkillDefinition(new SkillId(1), 0, 0, 1, 0, CastTargetKind.Self, BaseDamage: 1)),
        Invariants: InvariantOptions.Enabled);
}
