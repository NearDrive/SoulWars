using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class EncounterTriggerTests
{
    [Fact]
    [Trait("Category", "PR66")]
    public void OnTick_Fires_At_Exact_Tick()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = CreateState(new EncounterDefinition(
            new EncounterId(6601),
            "tick-trigger",
            1,
            new ZoneId(1),
            ImmutableArray.Create(
                new EncounterPhaseDefinition(ImmutableArray.Create(
                    new EncounterTriggerDefinition(
                        EncounterTriggerKind.OnTick,
                        AtTickOffset: 3,
                        Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.SpawnNpc, X: Fix32.FromInt(4), Y: Fix32.FromInt(4), Count: 1))))))));

        int baselineNpcCount = state.Zones[0].Entities.Count(e => e.Kind == EntityKind.Npc);
        for (int i = 0; i < 5; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        int afterNpcCount = state.Zones[0].Entities.Count(e => e.Kind == EntityKind.Npc);
        EncounterRuntimeState runtime = Assert.Single(state.EncounterRegistryOrEmpty.RuntimeStates);
        Assert.Equal(baselineNpcCount + 1, afterNpcCount);
        Assert.True(runtime.FiredTriggers[0]);
    }

    [Fact]
    [Trait("Category", "PR66")]
    public void OnHpBelowPct_Fires_Once()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = CreateState(new EncounterDefinition(
            new EncounterId(6602),
            "hp-trigger",
            1,
            new ZoneId(1),
            ImmutableArray.Create(
                new EncounterPhaseDefinition(ImmutableArray.Create(
                    new EncounterTriggerDefinition(
                        EncounterTriggerKind.OnHpBelowPct,
                        Target: EntityRef.Boss,
                        Pct: 70,
                        Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.SpawnNpc, X: Fix32.FromInt(5), Y: Fix32.FromInt(5), Count: 1))))))));

        state = EnterPlayers(state, new EntityId(10), new EntityId(13), new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)));
        int initialNpcCount = state.Zones[0].Entities.Count(e => e.Kind == EntityKind.Npc);

        for (int i = 0; i < 25; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
                new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(10), new ZoneId(1), TargetEntityId: new EntityId(100001)),
                new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(13), new ZoneId(1), TargetEntityId: new EntityId(100001)))));
        }

        int afterNpcCount = state.Zones[0].Entities.Count(e => e.Kind == EntityKind.Npc);
        EncounterRuntimeState runtime = Assert.Single(state.EncounterRegistryOrEmpty.RuntimeStates);
        Assert.Equal(initialNpcCount + 1, afterNpcCount);
        Assert.True(runtime.FiredTriggers[0]);
    }

    [Fact]
    [Trait("Category", "PR66")]
    public void OnEntityDeath_Fires_Once()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = CreateState(new EncounterDefinition(
            new EncounterId(6603),
            "death-trigger",
            1,
            new ZoneId(1),
            ImmutableArray.Create(
                new EncounterPhaseDefinition(ImmutableArray.Create(
                    new EncounterTriggerDefinition(
                        EncounterTriggerKind.OnEntityDeath,
                        Target: EntityRef.Boss,
                        Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.SpawnNpc, X: Fix32.FromInt(6), Y: Fix32.FromInt(6), Count: 1))))))));

        state = EnterPlayers(state, new EntityId(11), new EntityId(14), new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2)));
        int initialNpcCount = state.Zones[0].Entities.Count(e => e.Kind == EntityKind.Npc);

        for (int i = 0; i < 55; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
                new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(11), new ZoneId(1), TargetEntityId: new EntityId(100001)),
                new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(14), new ZoneId(1), TargetEntityId: new EntityId(100001)))));
        }

        int afterNpcCount = state.Zones[0].Entities.Count(e => e.Kind == EntityKind.Npc);
        EncounterRuntimeState runtime = Assert.Single(state.EncounterRegistryOrEmpty.RuntimeStates);
        Assert.Equal(initialNpcCount, afterNpcCount);
        Assert.True(runtime.FiredTriggers[0]);
    }

    private static WorldState EnterPlayers(WorldState state, EntityId first, EntityId second, Vec2Fix pos)
    {
        return Simulation.Step(CreateConfig(), state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, first, new ZoneId(1), SpawnPos: pos),
            new WorldCommand(WorldCommandKind.EnterZone, second, new ZoneId(1), SpawnPos: pos))));
    }

    private static WorldState CreateState(EncounterDefinition encounter)
    {
        ZoneDefinition zoneDef = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(32), Fix32.FromInt(32)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("boss", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))))),
            null,
            null,
            ImmutableArray.Create(encounter));

        return Simulation.CreateInitialState(CreateConfig(), new ZoneDefinitions(ImmutableArray.Create(zoneDef)));
    }

    private static SimulationConfig CreateConfig() => new(
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
