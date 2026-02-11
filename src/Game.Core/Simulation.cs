using System.Collections.Immutable;

namespace Game.Core;

public static class Simulation
{
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

        ImmutableArray<(WorldCommand Command, int OriginalIndex)> ordered = commands
            .Select((command, index) => (Command: command, OriginalIndex: index))
            .OrderBy(item => item.Command.ZoneId.Value)
            .ThenBy(item => item.Command.EntityId.Value)
            .ThenBy(item => CommandPriority(item.Command.Kind))
            .ThenBy(item => item.OriginalIndex)
            .ToImmutableArray();

        WorldState updated = state with { Tick = state.Tick + 1 };

        foreach ((WorldCommand command, _) in ordered)
        {
            ValidateCommand(command);

            if (!updated.TryGetZone(command.ZoneId, out ZoneState zone))
            {
                continue;
            }

            if (command.Kind is WorldCommandKind.EnterZone)
            {
                updated = RemoveEntityFromOtherZones(updated, command.EntityId, command.ZoneId);
                _ = updated.TryGetZone(command.ZoneId, out zone);
            }

            ZoneState nextZone = command.Kind switch
            {
                WorldCommandKind.EnterZone => ApplyEnterZone(config, zone, command),
                WorldCommandKind.LeaveZone => ApplyLeaveZone(zone, command),
                WorldCommandKind.MoveIntent => ApplyMoveIntent(config, zone, command),
                _ => zone
            };

            updated = updated.WithZoneUpdated(nextZone);
        }

        _ = config.MaxSpeed;

        return updated;
    }

    private static WorldState RemoveEntityFromOtherZones(WorldState state, EntityId entityId, ZoneId targetZoneId)
    {
        WorldState updated = state;

        foreach (ZoneState zone in state.Zones)
        {
            if (zone.Id.Value == targetZoneId.Value)
            {
                continue;
            }

            if (!zone.Entities.Any(entity => entity.Id.Value == entityId.Value))
            {
                continue;
            }

            ZoneState cleanedZone = zone with
            {
                Entities = zone.Entities
                    .Where(entity => entity.Id.Value != entityId.Value)
                    .OrderBy(entity => entity.Id.Value)
                    .ToImmutableArray()
            };

            updated = updated.WithZoneUpdated(cleanedZone);
        }

        return updated;
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

    private static int CommandPriority(WorldCommandKind kind) => kind switch
    {
        WorldCommandKind.EnterZone => 0,
        WorldCommandKind.MoveIntent => 1,
        WorldCommandKind.LeaveZone => 2,
        _ => 3
    };

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
            Vel: Vec2Fix.Zero);

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
