using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class CoreInvariantsTests
{
    [Fact]
    public void Validate_Throws_WhenZonesUnsorted()
    {
        TileMap map = new(2, 2, ImmutableArray.Create(TileKind.Empty, TileKind.Empty, TileKind.Empty, TileKind.Empty));
        WorldState world = new(
            0,
            ImmutableArray.Create(
                new ZoneState(new ZoneId(2), map, ImmutableArray<EntityState>.Empty),
                new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            ImmutableArray<EntityLocation>.Empty);

        Assert.Throws<InvariantViolationException>(() => CoreInvariants.Validate(world, tick: 0));
    }

    [Fact]
    public void Invariants_HealthBounds_Enforced()
    {
        SimulationConfig config = SimulationConfig.Default(42) with { ZoneCount = 1, NpcCountPerZone = 0 };
        WorldState state = Simulation.CreateInitialState(config);

        EntityId attackerId = new(1);
        EntityId targetId = new(2);
        Inputs setup = new(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, attackerId, new ZoneId(1)),
            new WorldCommand(WorldCommandKind.EnterZone, targetId, new ZoneId(1))));
        state = Simulation.Step(config, state, setup);

        for (int i = 0; i < 30; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
                new WorldCommand(WorldCommandKind.AttackIntent, attackerId, new ZoneId(1), TargetEntityId: targetId))));
        }

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone));
        Assert.DoesNotContain(zone.Entities, e => e.Id.Value == targetId.Value);
        CoreInvariants.Validate(state, state.Tick);

        EntityState invalid = new(
            Id: new EntityId(99),
            Pos: new Vec2Fix(Fix32.FromInt(1), Fix32.FromInt(1)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: -1,
            IsAlive: false,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 1,
            AttackCooldownTicks: 1,
            LastAttackTick: 0);

        WorldState corrupted = state.WithZoneUpdated(new ZoneState(new ZoneId(1), zone.Map, zone.Entities.Add(invalid)));
        Assert.Throws<InvariantViolationException>(() => CoreInvariants.Validate(corrupted, corrupted.Tick));
    }

    [Fact]
    public void Invariants_NoEntityInTwoZones()
    {
        TileMap map = new(3, 3, ImmutableArray.Create(
            TileKind.Empty, TileKind.Empty, TileKind.Empty,
            TileKind.Empty, TileKind.Empty, TileKind.Empty,
            TileKind.Empty, TileKind.Empty, TileKind.Empty));

        EntityState entity = new(
            Id: new EntityId(10),
            Pos: new Vec2Fix(Fix32.FromInt(1), Fix32.FromInt(1)),
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 1,
            AttackCooldownTicks: 1,
            LastAttackTick: 0);

        WorldState world = new(
            Tick: 5,
            Zones: ImmutableArray.Create(
                new ZoneState(new ZoneId(1), map, ImmutableArray.Create(entity)),
                new ZoneState(new ZoneId(2), map, ImmutableArray.Create(entity))),
            EntityLocations: ImmutableArray.Create(
                new EntityLocation(new EntityId(10), new ZoneId(1))));

        Assert.Throws<InvariantViolationException>(() => CoreInvariants.Validate(world, world.Tick));
    }

    [Fact]
    public void Fuzz_CombatTeleportMoves_NoCrash_AndInvariantsHold()
    {
        const int ticks = 1000;
        SimulationConfig config = SimulationConfig.Default(123) with
        {
            ZoneCount = 2,
            NpcCountPerZone = 3,
            MapWidth = 24,
            MapHeight = 24,
            Invariants = InvariantOptions.Enabled
        };

        WorldState state = Simulation.CreateInitialState(config);
        EntityId[] players = [new EntityId(1), new EntityId(2)];

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, players[0], new ZoneId(1)),
            new WorldCommand(WorldCommandKind.EnterZone, players[1], new ZoneId(2)))));

        SimRng rng = new(1337);

        for (int tick = 0; tick < ticks; tick++)
        {
            List<WorldCommand> commands = new();
            foreach (EntityId player in players)
            {
                if (!state.TryGetEntityZone(player, out ZoneId zoneId))
                {
                    continue;
                }

                int roll = rng.NextInt(0, 100);
                if (roll < 70)
                {
                    commands.Add(new WorldCommand(
                        WorldCommandKind.MoveIntent,
                        player,
                        zoneId,
                        MoveX: (sbyte)rng.NextInt(-1, 2),
                        MoveY: (sbyte)rng.NextInt(-1, 2)));
                }
                else if (roll < 90)
                {
                    List<EntityId> targets = new();
                    if (state.TryGetZone(zoneId, out ZoneState zone))
                    {
                        foreach (EntityState entity in zone.Entities)
                        {
                            targets.Add(entity.Id);
                        }
                    }

                    if (targets.Count > 0)
                    {
                        EntityId targetId = targets[rng.NextInt(0, targets.Count)];
                        commands.Add(new WorldCommand(WorldCommandKind.AttackIntent, player, zoneId, TargetEntityId: targetId));
                    }
                }
                else
                {
                    int toZone = zoneId.Value == 1 ? 2 : 1;
                    commands.Add(new WorldCommand(WorldCommandKind.TeleportIntent, player, zoneId, ToZoneId: new ZoneId(toZone)));
                }
            }

            state = Simulation.Step(config, state, new Inputs(commands.ToImmutableArray()));
            CoreInvariants.Validate(state, state.Tick);
        }

        uint checksumA = StateChecksum.Compute(state);
        WorldState rerun = Simulation.CreateInitialState(config);
        rerun = Simulation.Step(config, rerun, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, players[0], new ZoneId(1)),
            new WorldCommand(WorldCommandKind.EnterZone, players[1], new ZoneId(2)))));
        rng = new SimRng(1337);

        for (int tick = 0; tick < ticks; tick++)
        {
            List<WorldCommand> commands = new();
            foreach (EntityId player in players)
            {
                if (!rerun.TryGetEntityZone(player, out ZoneId zoneId))
                {
                    continue;
                }

                int roll = rng.NextInt(0, 100);
                if (roll < 70)
                {
                    commands.Add(new WorldCommand(WorldCommandKind.MoveIntent, player, zoneId,
                        MoveX: (sbyte)rng.NextInt(-1, 2), MoveY: (sbyte)rng.NextInt(-1, 2)));
                }
                else if (roll < 90)
                {
                    List<EntityId> targets = new();
                    if (rerun.TryGetZone(zoneId, out ZoneState zone))
                    {
                        foreach (EntityState entity in zone.Entities)
                        {
                            targets.Add(entity.Id);
                        }
                    }

                    if (targets.Count > 0)
                    {
                        commands.Add(new WorldCommand(WorldCommandKind.AttackIntent, player, zoneId,
                            TargetEntityId: targets[rng.NextInt(0, targets.Count)]));
                    }
                }
                else
                {
                    commands.Add(new WorldCommand(WorldCommandKind.TeleportIntent, player, zoneId,
                        ToZoneId: new ZoneId(zoneId.Value == 1 ? 2 : 1)));
                }
            }

            rerun = Simulation.Step(config, rerun, new Inputs(commands.ToImmutableArray()));
            CoreInvariants.Validate(rerun, rerun.Tick);
        }

        Assert.Equal(checksumA, StateChecksum.Compute(rerun));
    }
}
