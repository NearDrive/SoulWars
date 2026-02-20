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
        ImmutableArray<string> runA = RunScenario(config);
        ImmutableArray<string> runB = RunScenario(config);
        Assert.Equal(runA, runB);
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
            trace.Add(StateChecksum.Compute(state));
        }

        return trace.MoveToImmutable();
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
