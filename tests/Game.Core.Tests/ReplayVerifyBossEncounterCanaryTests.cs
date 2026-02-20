using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class ReplayVerifyBossEncounterCanaryTests
{
    [Fact]
    [Trait("Category", "PR66")]
    [Trait("Category", "ReplayVerify")]
    public void ReplayVerify_BossEncounter_Canary()
    {
        SimulationConfig config = CreateConfig();
        string baseline = RunWithOptionalRestart(config, null);
        string resumed = RunWithOptionalRestart(config, 30);
        Assert.Equal(baseline, resumed);
    }

    private static string RunWithOptionalRestart(SimulationConfig config, int? restartTick)
    {
        EncounterDefinition def = new(
            new EncounterId(6620),
            "boss-canary",
            1,
            new ZoneId(1),
            ImmutableArray.Create(
                new EncounterPhaseDefinition(ImmutableArray.Create(
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnHpBelowPct, Target: EntityRef.Boss, Pct: 50, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.SpawnNpc, X: Fix32.FromInt(8), Y: Fix32.FromInt(8), Count: 2)))))));

        ZoneDefinition zoneDef = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(32), Fix32.FromInt(32)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("boss", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))))),
            null,
            null,
            ImmutableArray.Create(def));

        WorldState state = Simulation.CreateInitialState(config, new ZoneDefinitions(ImmutableArray.Create(zoneDef)));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(20), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(21), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))))));

        for (int tick = 0; tick < 60; tick++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
                new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(20), new ZoneId(1), TargetEntityId: new EntityId(100001)),
                new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(21), new ZoneId(1), TargetEntityId: new EntityId(100001)))));

            if (restartTick.HasValue && tick + 1 == restartTick.Value)
            {
                byte[] snap = Game.Persistence.WorldStateSerializer.SaveToBytes(state);
                state = Game.Persistence.WorldStateSerializer.LoadFromBytes(snap);
            }
        }

        return StateChecksum.Compute(state);
    }

    private static SimulationConfig CreateConfig() => new(
        Seed: 67,
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
        SkillDefinitions: ImmutableArray<SkillDefinition>.Empty,
        Invariants: InvariantOptions.Enabled);
}
