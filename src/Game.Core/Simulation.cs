using System.Collections.Immutable;

namespace Game.Core;

public static class Simulation
{
    private const int DefaultMaxHp = 100;
    private const int DefaultAttackDamage = 10;
    private const int DefaultAttackCooldownTicks = 10;

    public static WorldState CreateInitialState(SimulationConfig config)
    {
        TileMap map = WorldGen.Generate(config, config.MapWidth, config.MapHeight);

        ZoneState localZone = new(
            Id: new ZoneId(1),
            Map: map,
            Entities: ImmutableArray<EntityState>.Empty);

        return new WorldState(
            Tick: 0,
            Zones: ImmutableArray.Create(localZone));
    }

    public static WorldState Step(SimulationConfig config, WorldState state, Inputs inputs)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inputs);

        ImmutableArray<WorldCommand> commands = inputs.Commands.IsDefault
            ? ImmutableArray<WorldCommand>.Empty
            : inputs.Commands;

        WorldState updated = state with { Tick = state.Tick + 1 };

        ImmutableArray<(ZoneId ZoneId, ImmutableArray<WorldCommand> Commands)> commandsByZone = commands
            .GroupBy(c => c.ZoneId.Value)
            .OrderBy(g => g.Key)
            .Select(g => (new ZoneId(g.Key), g.ToImmutableArray()))
            .ToImmutableArray();

        foreach ((ZoneId zoneId, ImmutableArray<WorldCommand> zoneCommands) in commandsByZone)
        {
            if (!updated.TryGetZone(zoneId, out ZoneState zone))
            {
                continue;
            }

            ZoneState nextZone = ProcessZoneCommands(config, updated.Tick, zone, zoneCommands);
            updated = updated.WithZoneUpdated(nextZone);
        }

        _ = config.MaxSpeed;

        return updated;
    }

    private static ZoneState ProcessZoneCommands(SimulationConfig config, int tick, ZoneState zone, ImmutableArray<WorldCommand> zoneCommands)
    {
        ZoneState current = zone;

        foreach (WorldCommand command in zoneCommands
                     .Where(c => c.Kind is WorldCommandKind.EnterZone)
                     .OrderBy(c => c.EntityId.Value))
        {
            ValidateCommand(command);
            current = ApplyEnterZone(config, current, command);
        }

        foreach (WorldCommand command in zoneCommands
                     .Where(c => c.Kind is WorldCommandKind.MoveIntent)
                     .OrderBy(c => c.EntityId.Value))
        {
            ValidateCommand(command);
            current = ApplyMoveIntent(config, current, command);
        }

        foreach (WorldCommand command in zoneCommands
                     .Where(c => c.Kind is WorldCommandKind.AttackIntent)
                     .OrderBy(c => c.EntityId.Value))
        {
            ValidateCommand(command);
            current = ApplyAttackIntent(tick, current, command);
        }

        foreach (WorldCommand command in zoneCommands
                     .Where(c => c.Kind is WorldCommandKind.LeaveZone)
                     .OrderBy(c => c.EntityId.Value))
        {
            ValidateCommand(command);
            current = ApplyLeaveZone(current, command);
        }

        return current;
    }

    private static void ValidateCommand(WorldCommand command)
    {
        if (command.Kind is WorldCommandKind.MoveIntent)
        {
            if (command.MoveX is < -1 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(command), "MoveX must be in range [-1..1].");
            }

            if (command.MoveY is < -1 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(command), "MoveY must be in range [-1..1].");
            }
        }
    }

    private static ZoneState ApplyEnterZone(SimulationConfig config, ZoneState zone, WorldCommand command)
    {
        bool exists = zone.Entities.Any(entity => entity.Id.Value == command.EntityId.Value);
        if (exists)
        {
            return zone;
        }

        Vec2Fix spawn = command.SpawnPos ?? FindSpawn(zone.Map, config.Radius);

        EntityState entity = new(
            Id: command.EntityId,
            Pos: spawn,
            Vel: Vec2Fix.Zero,
            MaxHp: DefaultMaxHp,
            Hp: DefaultMaxHp,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: DefaultAttackDamage,
            AttackCooldownTicks: DefaultAttackCooldownTicks,
            LastAttackTick: -DefaultAttackCooldownTicks);

        return zone with
        {
            Entities = zone.Entities
                .Add(entity)
                .OrderBy(e => e.Id.Value)
                .ToImmutableArray()
        };
    }

    private static ZoneState ApplyLeaveZone(ZoneState zone, WorldCommand command)
    {
        return zone with
        {
            Entities = zone.Entities
                .Where(entity => entity.Id.Value != command.EntityId.Value)
                .OrderBy(entity => entity.Id.Value)
                .ToImmutableArray()
        };
    }

    private static ZoneState ApplyMoveIntent(SimulationConfig config, ZoneState zone, WorldCommand command)
    {
        int entityIndex = -1;
        for (int i = 0; i < zone.Entities.Length; i++)
        {
            if (zone.Entities[i].Id.Value == command.EntityId.Value)
            {
                entityIndex = i;
                break;
            }
        }

        if (entityIndex < 0)
        {
            return zone;
        }

        EntityState entity = zone.Entities[entityIndex];
        if (!entity.IsAlive)
        {
            return zone;
        }

        PlayerInput playerInput = new(entity.Id, command.MoveX, command.MoveY);

        EntityState updated = Physics2D.Integrate(
            entity,
            playerInput,
            zone.Map,
            config.DtFix,
            config.MoveSpeed,
            config.Radius);

        ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(zone.Entities.Length);

        for (int i = 0; i < zone.Entities.Length; i++)
        {
            builder.Add(i == entityIndex ? updated : zone.Entities[i]);
        }

        return zone with
        {
            Entities = builder
                .ToImmutable()
                .OrderBy(e => e.Id.Value)
                .ToImmutableArray()
        };
    }

    private static ZoneState ApplyAttackIntent(int tick, ZoneState zone, WorldCommand command)
    {
        if (command.TargetEntityId is null)
        {
            return zone;
        }

        int attackerIndex = -1;
        int targetIndex = -1;

        for (int i = 0; i < zone.Entities.Length; i++)
        {
            EntityState entity = zone.Entities[i];
            if (entity.Id.Value == command.EntityId.Value)
            {
                attackerIndex = i;
            }

            if (entity.Id.Value == command.TargetEntityId.Value.Value)
            {
                targetIndex = i;
            }
        }

        if (attackerIndex < 0 || targetIndex < 0)
        {
            return zone;
        }

        EntityState attacker = zone.Entities[attackerIndex];
        EntityState target = zone.Entities[targetIndex];

        if (!attacker.IsAlive || !target.IsAlive)
        {
            return zone;
        }

        if (tick - attacker.LastAttackTick < attacker.AttackCooldownTicks)
        {
            return zone;
        }

        Fix32 dx = attacker.Pos.X - target.Pos.X;
        Fix32 dy = attacker.Pos.Y - target.Pos.Y;
        Fix32 distSq = (dx * dx) + (dy * dy);
        Fix32 rangeSq = attacker.AttackRange * attacker.AttackRange;

        if (distSq > rangeSq)
        {
            return zone;
        }

        EntityState updatedAttacker = attacker with { LastAttackTick = tick };

        int nextHp = Math.Max(0, target.Hp - attacker.AttackDamage);
        EntityState updatedTarget = target with
        {
            Hp = nextHp,
            IsAlive = nextHp > 0
        };

        ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(zone.Entities.Length);

        for (int i = 0; i < zone.Entities.Length; i++)
        {
            if (i == attackerIndex)
            {
                builder.Add(updatedAttacker);
                continue;
            }

            if (i == targetIndex)
            {
                if (updatedTarget.IsAlive)
                {
                    builder.Add(updatedTarget);
                }

                continue;
            }

            builder.Add(zone.Entities[i]);
        }

        return zone with
        {
            Entities = builder
                .ToImmutable()
                .OrderBy(e => e.Id.Value)
                .ToImmutableArray()
        };
    }

    private static Vec2Fix FindSpawn(TileMap map, Fix32 radius)
    {
        Fix32 half = new(Fix32.OneRaw / 2);

        for (int y = 1; y < map.Height - 1; y++)
        {
            for (int x = 1; x < map.Width - 1; x++)
            {
                if (map.Get(x, y) == TileKind.Solid)
                {
                    continue;
                }

                Vec2Fix candidate = new(Fix32.FromInt(x) + half, Fix32.FromInt(y) + half);
                if (!Physics2D.OverlapsSolidTile(candidate, radius, map))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException("Unable to find a valid spawn position.");
    }
}
