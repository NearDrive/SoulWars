using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class CombatSimulationTests
{
    [Fact]
    public void Combat_Damage_Applied()
    {
        SimulationConfig config = CreateConfig(123);
        WorldState state = SpawnDuel(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(1), new ZoneId(1), TargetEntityId: new EntityId(2)))));

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone));
        EntityState target = zone.Entities.Single(e => e.Id.Value == 2);
        Assert.Equal(90, target.Hp);
    }

    [Fact]
    public void Combat_Cooldown_Respected()
    {
        SimulationConfig config = CreateConfig(123);
        WorldState state = SpawnDuel(config);

        WorldCommand attack = new(WorldCommandKind.AttackIntent, new EntityId(1), new ZoneId(1), TargetEntityId: new EntityId(2));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(attack)));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(attack)));

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone));
        EntityState target = zone.Entities.Single(e => e.Id.Value == 2);
        Assert.Equal(90, target.Hp);
    }

    [Fact]
    public void Combat_TargetDies_Removed()
    {
        SimulationConfig config = CreateConfig(123);
        WorldState state = SpawnDuel(config);

        // Make kill deterministic in a single hit so the assertion does not depend on cooldown tick spacing.
        state = WithAttackDamage(state, zoneId: 1, entityId: 1, attackDamage: 100);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.AttackIntent, new EntityId(1), new ZoneId(1), TargetEntityId: new EntityId(2)))));

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone));
        Assert.DoesNotContain(zone.Entities, e => e.Id.Value == 2);
    }

    [Fact]
    public void Determinism_Combat_SameSeedSameInputs()
    {
        SimulationConfig config = CreateConfig(777);

        string a = RunCombatSequence(config);
        string b = RunCombatSequence(config);

        Assert.Equal(a, b);
    }

    private static WorldState SpawnDuel(SimulationConfig config)
    {
        WorldState state = Simulation.CreateInitialState(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2) + new Fix32(Fix32.OneRaw / 2))))));

        return state;
    }


    private static WorldState WithAttackDamage(WorldState state, int zoneId, int entityId, int attackDamage)
    {
        Assert.True(state.TryGetZone(new ZoneId(zoneId), out ZoneState zone));

        ZoneState updatedZone = zone.WithEntities(
            zone.Entities
                .Select(entity => entity.Id.Value == entityId ? entity with { AttackDamage = attackDamage } : entity)
                .ToImmutableArray());

        return state.WithZoneUpdated(updatedZone);
    }

    private static string RunCombatSequence(SimulationConfig config)
    {
        WorldState state = SpawnDuel(config);

        WorldCommand[] commands =
        [
            new(WorldCommandKind.AttackIntent, new EntityId(1), new ZoneId(1), TargetEntityId: new EntityId(2)),
            new(WorldCommandKind.AttackIntent, new EntityId(2), new ZoneId(1), TargetEntityId: new EntityId(1)),
            new(WorldCommandKind.MoveIntent, new EntityId(1), new ZoneId(1), MoveX: 1),
            new(WorldCommandKind.MoveIntent, new EntityId(2), new ZoneId(1), MoveX: -1),
            new(WorldCommandKind.AttackIntent, new EntityId(1), new ZoneId(1), TargetEntityId: new EntityId(2)),
            new(WorldCommandKind.AttackIntent, new EntityId(2), new ZoneId(1), TargetEntityId: new EntityId(1))
        ];

        foreach (WorldCommand command in commands)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(command)));
        }

        return StateChecksum.Compute(state);
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: 2,
        MapWidth: 16,
        MapHeight: 16,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
